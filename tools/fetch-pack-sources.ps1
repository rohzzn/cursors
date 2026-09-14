<#
  Downloads every upstream file tools\packs.recipe uses into -Destination, laid out the way tools\build-packs.ps1
  expects (about 250 MB downloaded, 570 MB once extracted). Release archives, the Debian package and license texts are checked against pinned SHA-256
  hashes; repository folders are fetched at pinned commits. Needs Windows PowerShell 5.1 and the tar.exe that ships
  with Windows 10 and 11.

    powershell -ExecutionPolicy Bypass -File tools\fetch-pack-sources.ps1 -Destination D:\cursor-sources
    powershell -ExecutionPolicy Bypass -File tools\build-packs.ps1 -Sources D:\cursor-sources

  Safe to re-run: finished downloads and extractions are kept. The script makes six GitHub API calls; set
  GITHUB_TOKEN if you hit the anonymous rate limit.
#>
param([Parameter(Mandatory = $true)][string]$Destination)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem

$Destination = [IO.Path]::GetFullPath($Destination)
$raw = Join-Path $Destination 'raw'
$work = Join-Path $Destination 'work'
$headers = @{ 'User-Agent' = 'cursors-pack-fetch' }
$failures = New-Object System.Collections.Generic.List[string]

function Save-Url([string]$Url, [string]$Path, [string]$Sha256) {
    if (Test-Path $Path) {
        if (-not $Sha256 -or (Get-FileHash $Path -Algorithm SHA256).Hash -eq $Sha256) { return $true }
        Remove-Item $Path -Force
    }
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    $part = "$Path.part"
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-WebRequest $Url -OutFile $part -UseBasicParsing -Headers $headers
            break
        } catch {
            $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
            if ($status -eq 404 -or $attempt -ge 3) {
                $failures.Add("$Url : $($_.Exception.Message)")
                if (Test-Path $part) { Remove-Item $part -Force }
                return $false
            }
            Start-Sleep -Seconds (3 * $attempt)
        }
    }
    if ($Sha256) {
        $actual = (Get-FileHash $part -Algorithm SHA256).Hash
        if ($actual -ne $Sha256) {
            $failures.Add("$Url : SHA-256 is $actual, expected $Sha256")
            Remove-Item $part -Force
            return $false
        }
    }
    Move-Item $part $Path -Force
    return $true
}

function Expand-Zip([string]$Zip, [string]$To) {
    if (Test-Path $To) { return }
    $part = "$To.part"
    if (Test-Path $part) { Remove-Item $part -Recurse -Force }
    [IO.Compression.ZipFile]::ExtractToDirectory($Zip, $part)
    Move-Item $part $To
}

# Extracts only the named top-level folders of a zip, each to its own (short) destination.
function Expand-ZipFolders([string]$Zip, [hashtable]$Map) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        foreach ($entry in $archive.Entries) {
            if (-not $entry.Name) { continue }
            foreach ($folder in $Map.Keys) {
                $prefix = "$folder/"
                $i = $entry.FullName.IndexOf($prefix)
                if ($i -lt 0 -or ($i -gt 0 -and $entry.FullName[$i - 1] -ne '/')) { continue }
                $dest = Join-Path $Map[$folder] ($entry.FullName.Substring($i + $prefix.Length).Replace('/', '\'))
                if (Test-Path $dest) { continue }
                New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true)
            }
        }
    } finally {
        $archive.Dispose()
    }
}

# Windows' tar can't create the symlinks Linux themes use; it skips them with an error, so the links are recorded in
# _symlinks.tsv for PackTool instead.
function Expand-Tar([string]$Archive, [string]$To) {
    if (Test-Path $To) { return }
    $part = "$To.part"
    if (Test-Path $part) { Remove-Item $part -Recurse -Force }
    New-Item -ItemType Directory -Force $part | Out-Null
    cmd /c "tar -xf `"$Archive`" -C `"$part`" 2> `"$part\_tar-errors.txt`""
    $links = @(cmd /c "tar -tvf `"$Archive`"" | ForEach-Object { if ($_ -match '^l.*\s(\S+) -> (\S+)$') { "$($Matches[1])`t$($Matches[2])" } })
    [IO.File]::WriteAllLines((Join-Path $part '_symlinks.tsv'), [string[]]$links)
    Move-Item $part $To
}

# Symlinks in a repository download as small text files holding their target; PackTool follows those.
function Save-RepoFolder([string]$Repo, [string]$Commit, [string]$Folder) {
    $api = @{} + $headers
    if ($env:GITHUB_TOKEN) { $api['Authorization'] = "Bearer $env:GITHUB_TOKEN" }
    $tree = Invoke-RestMethod "https://api.github.com/repos/$Repo/git/trees/${Commit}?recursive=1" -Headers $api
    $dir = Join-Path $raw ($Repo.Replace('/', '__'))
    foreach ($entry in $tree.tree) {
        if ($entry.type -ne 'blob' -or -not $entry.path.StartsWith($Folder)) { continue }
        Save-Url "https://raw.githubusercontent.com/$Repo/$Commit/$($entry.path)" (Join-Path $dir $entry.path.Replace('/', '\')) $null | Out-Null
    }
}

# ---- GitHub release assets: repo, tag, file, SHA-256 -----------------------------------------------------
$releases = @(
    @('ful1e5/Bibata_Cursor', 'v2.0.7', 'Bibata-Modern-Classic-Windows.zip', 'b98eca4077d6e7796ff7e710e4d220ca13439f1118160d1e7b3db9e91fdec2be'),
    @('ful1e5/Bibata_Cursor', 'v2.0.7', 'Bibata-Modern-Ice-Windows.zip', '0045e40324da5b540b3bee260f53d0792df62be9cdef91655a024ae9f151bd04'),
    @('ful1e5/Bibata_Cursor', 'v2.0.7', 'Bibata-Modern-Amber-Windows.zip', '5151e0f8648d00fb734fea501def12b5cc1fbc65ba12a6e28db2e1295ebf8961'),
    @('ful1e5/Bibata_Cursor', 'v2.0.7', 'Bibata-Original-Classic-Windows.zip', '5b1d47004039f5c844f0e21fa3498f6396790e9133ea7f8396e6487a3000a34e'),
    @('ful1e5/Bibata_Cursor', 'v2.0.7', 'Bibata-Original-Ice-Windows.zip', '868f2afc0fd91d334ec71962c339606defa5d85ce58fccc04485d54a4ac71aa2'),
    @('ful1e5/Bibata_Extra_Cursor', 'v1.0.1', 'Bibata-Modern-DodgerBlue-Windows.zip', '840aa39672bad81d9d9c09b62789de340f32d7abce1f52b14cfab6035570bdfa'),
    @('ful1e5/Bibata_Extra_Cursor', 'v1.0.1', 'Bibata-Modern-Pink-Windows.zip', '155670c39fdb72682bb85ef51b6b4963ef797d9eac0ef494b82d63df5219ee2b'),
    @('ful1e5/Bibata_Extra_Cursor', 'v1.0.1', 'Bibata-Modern-Turquoise-Windows.zip', '8327537896e96dd2d60f2baeb26c4518ead220ac8b1a87546a407fa117ae0548'),
    @('ful1e5/Bibata_Cursor_Rainbow', 'v1.1.2', 'Bibata-Rainbow-Modern-Windows.zip', 'b94c6b42c634883443facd5d8f7b0a1f248d3401880e509cd1afc88f9c909046'),
    @('ful1e5/Bibata_Cursor_Rainbow', 'v1.1.2', 'Bibata-Rainbow-Original-Windows.zip', 'b23ff7973ad2ae4924d962b55ef11bd87d07f77173b99fb4753a483d1265eb30'),
    @('ful1e5/Bibata-Bee-Cursor', 'v1.0.0', 'Bibata-Bee-Modern-Windows.zip', '7a66db8cf3c285a97dfe088d5ef69550ae3f8cf854b73a2f1731f4844cacfa22'),
    @('ful1e5/Bibata-Zebra-Cursor', 'v1.0.0', 'Bibata-Zebra-Modern-Windows.zip', 'ac86b43124bfe1f5183dbe5d8efa931de713be26914a2c9645df2ab97b115f62'),
    @('ful1e5/apple_cursor', 'v2.0.1', 'macOS-Windows.zip', '64a2c74780908954a2ec497f67919709280ebb8ebdf3e3b9336bac071589c9de'),
    @('ful1e5/apple_cursor', 'v2.0.1', 'macOS-White-Windows.zip', '241585cf0f2476fc89b1fc6f6a773e06749ae35cff55f6a0daed5722a3b1c794'),
    @('ful1e5/XCursor-pro', 'v2.0.2', 'XCursor-Pro-Dark-Windows.zip', '8b3235a6700017934f9092907ead683faf2e25de5d7934f773c44e04de6d8cc0'),
    @('ful1e5/XCursor-pro', 'v2.0.2', 'XCursor-Pro-Light-Windows.zip', '39c2074727bd6f1f3a6e4f1faf5f4bb3a08a314db91e020c5a405cfc94a356d1'),
    @('ful1e5/XCursor-pro', 'v2.0.2', 'XCursor-Pro-Red-Windows.zip', '4938ebcf5fc71279bc2335ab2ae4692fa6febb63bcf514d08ff911c396f21a26'),
    @('ful1e5/Google_Cursor', 'v2.0.0', 'GoogleDot-Black-Windows.zip', '10876e7c232e19e0805aba1aa2ca4393d047e8091cf3fe0266db567078bf2a71'),
    @('ful1e5/Google_Cursor', 'v2.0.0', 'GoogleDot-White-Windows.zip', 'e020a52418d0b321d8e905841d16414236424b2d80a338319382daaabda8e025'),
    @('ful1e5/Google_Cursor', 'v2.0.0', 'GoogleDot-Blue-Windows.zip', '0fbecb1659e2c01ff6f5028aff29b0696e2b01b39a81f084f7e713b92443baf3'),
    @('ful1e5/fuchsia-cursor', 'v2.0.1', 'Fuchsia-Windows.zip', '4abdbadb36e0a24410792a8f372a3eaacf9b08f63c4a0ad29170c89e8e435c68'),
    @('ful1e5/fuchsia-cursor', 'v2.0.1', 'Fuchsia-Pop-Windows.zip', '7b1064c600f1fa7a843ef1348f8f0b9eb60ce4bd4926d9a604812a54a7b3a052'),
    @('ful1e5/banana-cursor', 'v2.0.0', 'Banana-Windows.zip', '854c66d43ae6783ebad9c240f1dee187791d05a5920248acdd642c864159f2f9'),
    @('ful1e5/banana-cursor', 'v2.0.0', 'Banana-Blue-Windows.zip', '274f9a1754ef4cc0df7c203ce54d3c1cd1bf070cbcee7e558766e5fc8a2008c5'),
    @('ful1e5/BreezeX_Cursor', 'v2.0.1', 'BreezeX-Black-Windows.zip', '6f9428a29ca73f66710ea26a1c8c6b80f6ad4bda628af1aef889e9c6335005e1'),
    @('ful1e5/BreezeX_Cursor', 'v2.0.1', 'BreezeX-Light-Windows.zip', '743ef8050af98730fa370a40ff2040c3c4524e250b8e1642e4d09d7c9fc784ba'),
    @('ful1e5/notwaita-cursor', 'v1.0.0-alpha1', 'Notwaita-Black-Windows.zip', '0b195d96482ebf48fbadbcf89b1d501ee725edffe0a3b9a2779df5a6df8090b8'),
    @('ful1e5/notwaita-cursor', 'v1.0.0-alpha1', 'Notwaita-White-Windows.zip', '76ef5c24d7be6b9216897582821489ab2b61bfd77007c880b789e2d535518212'),
    @('guillaumeboehm/Nordzy-cursors', 'v2.4.0', 'Nordzy-cursors_windows.zip', '1396ed4e6777a9965682abe4a2475b53c0de70f938612141943705b804f7f28b'),
    @('guillaumeboehm/Nordzy-cursors', 'v2.4.0', 'Nordzy-cursors-white_windows.zip', '83b9f9bb22887b275008591b9300bf5baa518a264d967c059a7ba9f8506e254c'),
    @('Tech-Tac/aosp-cursors', '1.3.1', 'aosp-cursors-windows-1.3.1.zip', '9cfe34d56f6c7d6c471d29e89e858b5a6da1ee38198e0d84881685c4c26393f6'),
    @('catppuccin/cursors', 'v2.0.0', 'catppuccin-mocha-dark-cursors.zip', 'a4d976491bdb1b1311b2de88327cad3f1c66c2d9da896e0c56362a660c802585'),
    @('catppuccin/cursors', 'v2.0.0', 'catppuccin-latte-pink-cursors.zip', 'ddd89d8fe90202f8041445f0a404c9bed09401be01f742fd7028f328d9f47d1e'),
    @('catppuccin/cursors', 'v2.0.0', 'catppuccin-macchiato-mauve-cursors.zip', 'e7388debd7694c0da59aaa1d4f37d98517ff9cf231d15b9540e941db3543c1d9'),
    @('useless-anvil/retrosmart-cursor', 'v2.0.1', 'retrosmart-cursor-classic-v2.0.1-windows.zip', '59643023f0569dfb4a45804d05bf15ce0347555a381dbe868ce4d5c59e28a78a'),
    @('phisch/phinger-cursors', 'v2.1', 'phinger-cursors-variants.tar.bz2', 'ddb7310c62bf8e0e2798a24f8a867e4af7b17a39757ba45c85e13f3988f646fc')
)

Write-Host "Downloading release archives..."
foreach ($r in $releases) {
    $dir = Join-Path $raw ($r[0].Replace('/', '__'))
    $file = Join-Path $dir $r[2]
    if (-not (Save-Url "https://github.com/$($r[0])/releases/download/$($r[1])/$($r[2])" $file $r[3])) { continue }
    if ($file.EndsWith('.zip') -and $r[0] -ne 'useless-anvil/retrosmart-cursor') {
        Expand-Zip $file (Join-Path (Join-Path $work ($r[0].Replace('/', '__'))) ([IO.Path]::GetFileNameWithoutExtension($file)))
    }
}

# Retrosmart's paths exceed MAX_PATH once nested, so its three variants go to short folders.
$retrosmart = Join-Path $raw 'useless-anvil__retrosmart-cursor\retrosmart-cursor-classic-v2.0.1-windows.zip'
if (Test-Path $retrosmart) {
    Expand-ZipFolders $retrosmart @{
        'retrosmart-xcursor-win-ish-classic-shadow' = (Join-Path $work 'rs\win')
        'retrosmart-xcursor-mac-ish-classic-shadow' = (Join-Path $work 'rs\mac')
        'retrosmart-xcursor-cur-font-classic'       = (Join-Path $work 'rs\term')
    }
}
$phinger = Join-Path $raw 'phisch__phinger-cursors\phinger-cursors-variants.tar.bz2'
if (Test-Path $phinger) { Expand-Tar $phinger (Join-Path $work 'phinger-full') }

# ---- Other hosts -----------------------------------------------------------------------------------------
Write-Host "Downloading Kenney, Hackneyed and ComixCursors..."
$others = @(
    @('https://kenney.nl/media/pages/assets/cursor-pack/461b29df18-1717599281/kenney_cursor-pack.zip', 'kenney\kenney_cursor-pack.zip', 'baa87634f3cfd294b0c83e70222053aab2f56d19f78eca4067b9b38a555269cc'),
    @('https://kenney.nl/media/pages/assets/cursor-pixel-pack/092f2b012b-1720601332/kenney_cursor-pixel-pack.zip', 'kenney\kenney_cursor-pixel-pack.zip', '357071bc497e6069563f1b44779bbf5c873d5ef0daade9ba38ef0612cc6f44b4'),
    @('https://gitlab.com/-/project/6703061/uploads/63cae7c67920af90c7906a0aa32cb7fa/Hackneyed-Windows-0.9.3.zip', 'Enthymeme__hackneyed-x11-cursors\Hackneyed-Windows-0.9.3.zip', '43980eb1660e1d9da5d20d755d2cf0d3ff120c2615cd9c17f00d7e8e467a4b6d'),
    @('https://gitlab.com/-/project/6703061/uploads/34801c60d5095019c92e2d5a2d89efaa/Hackneyed-Dark-Windows-0.9.3.zip', 'Enthymeme__hackneyed-x11-cursors\Hackneyed-Dark-Windows-0.9.3.zip', '55b755f27e2411ffdbc3a8c87400024f7fc8c269abd8d4debd9ba2aee647c529'),
    @('https://deb.debian.org/debian/pool/main/c/comixcursors/comixcursors-righthanded_0.9.1-3_all.deb', 'debian__comixcursors\comixcursors-righthanded_0.9.1-3_all.deb', '6865dfe5b256aa157b4ae737256331272a73973ff8ff2db04809596548ec295d')
)
foreach ($o in $others) {
    $file = Join-Path $raw $o[1]
    if (-not (Save-Url $o[0] $file $o[2])) { continue }
    if ($file.EndsWith('.zip')) {
        Expand-Zip $file (Join-Path (Join-Path $work (Split-Path $o[1])) ([IO.Path]::GetFileNameWithoutExtension($file)))
    }
}
$deb = Join-Path $raw 'debian__comixcursors\comixcursors-righthanded_0.9.1-3_all.deb'
$debDir = Join-Path $work 'comix-deb'
if ((Test-Path $deb) -and -not (Test-Path (Join-Path $work 'comix-data'))) {
    New-Item -ItemType Directory -Force $debDir | Out-Null
    cmd /c "tar -xf `"$deb`" -C `"$debDir`""
    $data = Get-ChildItem $debDir -Filter 'data.tar*' | Select-Object -First 1
    if ($data) { Expand-Tar $data.FullName (Join-Path $work 'comix-data') } else { $failures.Add("$deb : no data.tar inside") }
}

# ---- Repository folders at pinned commits ----------------------------------------------------------------
Write-Host "Downloading repository folders..."
Save-RepoFolder 'keeferrourke/capitaine-cursors' '06c88433662a4004cf56a6e471b523a0a8880be0' '.windows/'
Save-RepoFolder 'vinceliuice/Vimix-cursors' '9bc292f40904e0a33780eda5c5d92eb9a1154e9c' '.windows/'
Save-RepoFolder 'emvaized/modern_inverted_mouse_cursors' '77652f4940764e607a433c88baac1e208aecf254' 'src/'
Save-RepoFolder 'vinceliuice/WhiteSur-cursors' 'e190baf618ed95ee217d2fd45589bd309b37672b' 'dist/cursors/'
Save-RepoFolder 'yeyushengfan258/Future-cursors' '587c14d2f5bd2dc34095a4efbb1a729eb72a1d36' 'dist/cursors/'
Save-RepoFolder 'yeyushengfan258/Lyra-Cursors' 'c096c54034f95bd35699b3226250e5c5ec015d9a' 'dist/cursors/'

# The translucent Bibata repository is large; only the cursors the recipe maps are fetched.
$translucent = 'https://raw.githubusercontent.com/silica-dev/Bibata_Cursor_Translucent/34df75618a08e9a67592c31397ec843b84dc405f'
$names = 'default', 'help', 'progress', 'wait', 'crosshair', 'text', 'pencil', 'not-allowed', 'size_ver', 'size_hor',
         'size_fdiag', 'size_bdiag', 'fleur', 'up-arrow', 'pointer'
foreach ($variant in 'Bibata_Ghost', 'Bibata_Spirit', 'Bibata_Tinted') {
    foreach ($name in $names) {
        Save-Url "$translucent/$variant/cursors/$name" (Join-Path $raw "bibata-translucent\$variant\cursors\$name") $null | Out-Null
    }
}

# ---- License texts copied into packs\LICENSES --------------------------------------------------------------
Write-Host "Downloading license texts..."
$licenses = @(
    @('https://raw.githubusercontent.com/ful1e5/Bibata_Cursor/35ccfe209a808e40d6c2ca60a46cbe4faf68b690/LICENSE', 'ful1e5__Bibata_Cursor\_LICENSE', 'fe92c7ac268ce17193b98298336d3793a843a94eba9efc7ad112f9f48ad9b214'),
    @('https://raw.githubusercontent.com/catppuccin/cursors/a7eb08527dcce01010fa0ec46fa2bc4c3154f0d4/LICENSE', 'catppuccin__cursors\_LICENSE', '8177f97513213526df2cf6184d8ff986c675afb514d4e68a404010521b880643'),
    @('https://raw.githubusercontent.com/ful1e5/notwaita-cursor/db037306bbd731ae8d629d85a68269cc78ffd2b5/COPYING', 'ful1e5__notwaita-cursor\_COPYING', '51f83b2b4e9ed2beebc9b8eed2ecad29314a58ed8c5184b22ad8f9df703d3ce5'),
    @('https://raw.githubusercontent.com/ful1e5/notwaita-cursor/db037306bbd731ae8d629d85a68269cc78ffd2b5/COPYING_LGPL', 'ful1e5__notwaita-cursor\_COPYING_LGPL', 'da7eabb7bafdf7d3ae5e9f223aa5bdc1eece45ac569dc21b3b037520b4464768'),
    @('https://raw.githubusercontent.com/phisch/phinger-cursors/1e674f9a86d768de9f7dc93bb6d9685e25ce9655/LICENSE', 'phisch__phinger-cursors\_LICENSE', '87a816969906840bf7af8d4d01cdfad4741b18946365e1f286007935509f2edb'),
    @('https://raw.githubusercontent.com/emvaized/modern_inverted_mouse_cursors/77652f4940764e607a433c88baac1e208aecf254/LICENSE', 'emvaized__modern_inverted_mouse_cursors\_LICENSE', 'd977112e5c9962b6d659b25e3852e9e5f88313f78676546076c4a84a5d22309e'),
    @('https://raw.githubusercontent.com/keeferrourke/capitaine-cursors/06c88433662a4004cf56a6e471b523a0a8880be0/COPYING', 'keeferrourke__capitaine-cursors\_COPYING', 'd4ae93fd33a12e778bca17b9591f4b7cb5916267f432edcba746c528d270746f'),
    @('https://raw.githubusercontent.com/useless-anvil/retrosmart-cursor/963de7cbf44b0f3b035569a5b67cd2e3f8b262b3/COPYING', 'useless-anvil__retrosmart-cursor\_COPYING', 'c8a0d199930384dc0c523459ed05268561c8c3b915cb271ef6f5f1f36b5818a9'),
    @('https://gitlab.com/Enthymeme/hackneyed-x11-cursors/-/raw/master/LICENSE', 'Enthymeme__hackneyed-x11-cursors\LICENSE', 'f3850afff58a05205796f317e3e753b73fda50c89e8d81a03c639689f6aeaf1d')
)
foreach ($l in $licenses) { Save-Url $l[0] (Join-Path $raw $l[1]) $l[2] | Out-Null }

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "$($failures.Count) download(s) failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
$mb = [math]::Round((Get-ChildItem $Destination -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "Sources ready in $Destination ($mb MB). Next: tools\build-packs.ps1 -Sources `"$Destination`""
