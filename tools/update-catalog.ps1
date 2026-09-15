<#
.SYNOPSIS
    Builds packs\catalog.tsv, the Community section of the app: the most-downloaded complete cursor sets on
    rw-designer.com.

.DESCRIPTION
    Reads only rw-designer's public listing pages (/cursor-library/set-N) and set pages (/cursor-set/<slug>), which the
    site's robots.txt allows, one request at a time with a pause between requests. No cursor files are downloaded; the
    app downloads a set from rw-designer.com only when someone clicks it. Pages are cached, so an interrupted run picks up
    where it stopped.

    A set is listed when the site offers it as one download, it has a Normal Select pointer, and it covers at least
    -MinRoles cursor roles. Sets are ranked by download count.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\update-catalog.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\update-catalog.ps1 -Pages 1 -Count 5 -Output $env:TEMP\catalog-test.tsv
#>
[CmdletBinding()]
param(
    [int]$Count = 500,
    [int]$MinRoles = 3,
    [string]$Output, # default: packs\catalog.tsv in this repository
    [string]$Cache = (Join-Path $env:TEMP 'cursors-catalog-cache'),
    [int]$DelayMs = 1000,
    [int]$MaxCacheHours = 72,
    [int]$Pages = 0 # listing pages to read; 0 reads all of them
)

$ErrorActionPreference = 'Stop'
if (-not $Output) { $Output = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'packs\catalog.tsv' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$site = 'https://www.rw-designer.com'
$userAgent = 'Cursors catalog updater (+https://github.com/rohzzn/cursors)'
New-Item -ItemType Directory -Force $Cache | Out-Null

$script:lastRequest = [DateTime]::MinValue
$script:requests = 0

# Returns the page's HTML, or $null when the site says it doesn't exist.
function Get-Page([string]$path) {
    $file = Join-Path $Cache (($path.Trim('/') -replace '[^A-Za-z0-9._-]', '_') + '.html')
    if ((Test-Path $file) -and ((Get-Date) - (Get-Item $file).LastWriteTime).TotalHours -lt $MaxCacheHours) {
        return [IO.File]::ReadAllText($file, [Text.Encoding]::UTF8)
    }
    for ($attempt = 1; ; $attempt++) {
        $wait = $DelayMs - ((Get-Date) - $script:lastRequest).TotalMilliseconds
        if ($wait -gt 0) { Start-Sleep -Milliseconds ([int]$wait) }
        $script:lastRequest = Get-Date
        $script:requests++
        $client = New-Object Net.WebClient
        $client.Headers['User-Agent'] = $userAgent
        try {
            $html = [Text.Encoding]::UTF8.GetString($client.DownloadData($site + $path))
            [IO.File]::WriteAllText($file, $html, (New-Object Text.UTF8Encoding $false))
            return $html
        }
        catch [Net.WebException] {
            $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
            if ($status -eq 404 -or $status -eq 410) { return $null }
            if ($attempt -ge 4) { throw "Giving up on $path after $attempt attempts: $($_.Exception.Message)" }
            $backoff = 5 * [math]::Pow(3, $attempt - 1)
            Write-Warning "$path failed ($($_.Exception.Message)); retrying in $backoff s"
            Start-Sleep -Seconds $backoff
        }
        finally {
            $client.Dispose()
        }
    }
}

function Clean([string]$text) {
    $text = [Net.WebUtility]::HtmlDecode(($text -replace '<[^>]+>', ''))
    return ($text -replace '[\t\r\n]+', ' ' -replace '\s{2,}', ' ').Trim()
}

# ---- 1. Every set in the library, with its download count --------------------------------------------------------
$first = Get-Page '/cursor-library/set-0'
$lastOffset = ([regex]::Matches($first, 'href="/cursor-library/set-(\d+)"') | ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Maximum).Maximum
if ($Pages -gt 0) { $lastOffset = [math]::Min($lastOffset, ($Pages - 1) * 40) }
$teaser = '<div class="itemteaser">(?<flags>(?:<span class="\w+" title="[^"]*"></span>)*)<a class="item" href="/cursor-set/(?<slug>[^"]+)">.*?<span class="setname">(?<name>.*?)</span></a><div>(?:<span class="author">by (?<author>.*?)</span>)?<span class="downloads" title="(?<downloads>[\d,]+) downloads?'

$sets = @{}
$adult = New-Object Collections.Generic.HashSet[string]
for ($offset = 0; $offset -le $lastOffset; $offset += 40) {
    $html = if ($offset -eq 0) { $first } else { Get-Page "/cursor-library/set-$offset" }
    if (-not $html) { continue }
    foreach ($m in [regex]::Matches($html, $teaser, 'Singleline')) {
        $slug = $m.Groups['slug'].Value
        # Sets the site marks as adult content are never listed.
        if ($m.Groups['flags'].Value -match 'maturewarn') { [void]$adult.Add($slug); continue }
        if ($sets.ContainsKey($slug)) { continue }
        $sets[$slug] = [pscustomobject]@{
            Slug      = $slug
            Name      = (Clean $m.Groups['name'].Value) -replace '\s+Cursors?$', ''
            Author    = Clean $m.Groups['author'].Value
            Downloads = [int]($m.Groups['downloads'].Value -replace ',', '')
        }
    }
    $page = $offset / 40 + 1
    if ($page % 25 -eq 0 -or $offset -ge $lastOffset) { "listing page $page of $($lastOffset / 40 + 1): $($sets.Count) sets" }
}
if ($sets.Count -eq 0) { throw 'No cursor sets were found on the listing pages; the site layout may have changed.' }

# ---- 2. The most-downloaded sets that make a usable scheme -----------------------------------------------------------
$cell = 'id="cellcu(?<id>\d+)" class="itemteaser ?(?<class>[^"]*)"><a class="download" href="/cursor-download/\d+/'
$rows = New-Object Collections.Generic.List[object]
$skipped = [ordered]@{ 'no longer online' = 0; 'no set download' = 0; 'no normal pointer' = 0; "fewer than $MinRoles roles" = 0 }
$checked = 0
foreach ($set in ($sets.Values | Sort-Object @{ Expression = 'Downloads'; Descending = $true }, Slug)) {
    if ($rows.Count -ge $Count) { break }
    $html = Get-Page "/cursor-set/$($set.Slug)"
    $checked++
    if ($checked % 50 -eq 0) { "checked $checked set pages: $($rows.Count) listed, $($script:requests) requests so far" }
    if (-not $html) { $skipped['no longer online']++; continue }
    if ($html -notmatch ('/cursor-downloadset/' + [regex]::Escape($set.Slug) + '\.zip')) { $skipped['no set download']++; continue }

    # rw-designer tags each cursor with its role (curarrow, curlink, ...); the first cursor of a role is its main one.
    $roles = @{}
    $cursors = 0
    foreach ($c in [regex]::Matches($html, $cell)) {
        $cursors++
        $class = ($c.Groups['class'].Value -split '\s+' | Where-Object { $_ -like 'cur*' } | Select-Object -First 1)
        if ($class -and -not $roles.ContainsKey($class)) { $roles[$class] = $c.Groups['id'].Value }
    }
    if (-not $roles.ContainsKey('curarrow')) { $skipped['no normal pointer']++; continue }
    if ($roles.Count -lt $MinRoles) { $skipped["fewer than $MinRoles roles"]++; continue }

    $license = [regex]::Match($html, 'under\s+the\s*<strong>(.*?)</strong>', 'Singleline')
    $rows.Add([pscustomobject]@{
        Slug      = $set.Slug
        Name      = $set.Name
        Author    = $set.Author
        Downloads = $set.Downloads
        License   = if ($license.Success) { Clean $license.Groups[1].Value } else { '' }
        Cursors   = $cursors
        Roles     = $roles.Count
        Arrow     = $roles['curarrow']
        Link      = $roles['curlink']
        Text      = $roles['curtext']
        Busy      = $roles['curbusy']
    })
}

# ---- 3. Write the catalog ------------------------------------------------------------------------------------------
$lines = New-Object Collections.Generic.List[string]
$lines.Add('# Community catalog for Cursors: the most-downloaded complete cursor sets on rw-designer.com, by download count.')
$lines.Add("# Generated by tools/update-catalog.ps1 on $((Get-Date).ToString('yyyy-MM-dd')) from $($sets.Count) sets. It lists names and links only;")
$lines.Add('# the app downloads a set from rw-designer.com when it is clicked. Each set is licensed by its author, as shown on its page.')
$lines.Add("slug`tname`tauthor`tdownloads`tlicense`tcursors`troles`tarrow`tlink`ttext`tbusy")
foreach ($r in $rows) {
    $lines.Add((@($r.Slug, $r.Name, $r.Author, $r.Downloads, $r.License, $r.Cursors, $r.Roles, $r.Arrow, $r.Link, $r.Text, $r.Busy) -join "`t"))
}
New-Item -ItemType Directory -Force (Split-Path $Output -Parent) | Out-Null
[IO.File]::WriteAllText($Output, ($lines -join "`r`n") + "`r`n", (New-Object Text.UTF8Encoding $false))

''
"Listed $($rows.Count) of $($sets.Count) sets after checking $checked set pages ($($script:requests) requests). $($adult.Count) sets marked as adult content were left out."
foreach ($reason in $skipped.Keys) { "  skipped, $($reason): $($skipped[$reason])" }
if ($rows.Count -gt 0) { "  downloads range: $($rows[0].Downloads) to $($rows[$rows.Count - 1].Downloads)" }
"Wrote $Output"
