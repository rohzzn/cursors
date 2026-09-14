<#
  Rebuilds packs\ (the bundled cursor library) from upstream sources and verifies every pack.

    powershell -ExecutionPolicy Bypass -File tools\build-packs.ps1 -Sources <folder> [-ApplyTest]

  <folder> holds the upstream sources, as downloaded by tools\fetch-pack-sources.ps1 (see tools\SOURCES.md).
  Output: packs\<id>\ (cursor files + pack.ini), packs\NOTICE.md, packs\LICENSES\, and contact sheets plus a
  report in obj\pack-report\. -ApplyTest also applies every pack for real and confirms Windows loaded each cursor
  (your cursor settings are restored afterwards).
#>
param(
    [Parameter(Mandatory = $true)][string]$Sources,
    [switch]$ApplyTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$obj = Join-Path $root 'obj'
$staging = Join-Path $obj 'packs-staging'
$report = Join-Path $obj 'pack-report'
$recipe = Join-Path $PSScriptRoot 'packs.recipe'

function Find-Csc {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Roslyn\csc.exe' | Select-Object -First 1
        if ($found) { return $found }
    }
    $candidates = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio" `
        -Recurse -Filter csc.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like '*\Roslyn\csc.exe' }
    return ($candidates | Sort-Object FullName -Descending | Select-Object -First 1).FullName
}

# ---- Compile PackTool ------------------------------------------------------------------------------------
$csc = Find-Csc
if (-not $csc) { throw 'Roslyn csc.exe not found (install Visual Studio Build Tools).' }
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$toolDir = Join-Path $obj 'PackTool'
New-Item -ItemType Directory -Force $toolDir | Out-Null
$exe = Join-Path $toolDir 'PackTool.exe'
$refs = 'mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms' | ForEach-Object { "/r:$fw\$_.dll" }
# (Not $sources: PowerShell names are case-insensitive and that would overwrite -Sources.)
$csFiles = @('CursorImage.cs', 'CursorPack.cs', 'PackDetection.cs', 'Settings.cs' | ForEach-Object { Join-Path $root "src\$_" }) +
           @(Get-ChildItem (Join-Path $PSScriptRoot 'PackTool') -Filter *.cs | ForEach-Object FullName)
& $csc /nologo /noconfig /nostdlib+ /target:exe /platform:x64 /optimize+ /langversion:latest "/out:$exe" @refs @csFiles
if ($LASTEXITCODE -ne 0) { throw 'PackTool failed to compile' }

# ---- Build and verify ------------------------------------------------------------------------------------
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
& $exe build $recipe $Sources $staging
if ($LASTEXITCODE -ne 0) { throw "$LASTEXITCODE pack(s) failed to build" }
& $exe verify $staging $report
if ($LASTEXITCODE -ne 0) { throw "verification reported $LASTEXITCODE problem(s); see $report\report.md" }
if ($ApplyTest) {
    & $exe applytest $staging
    if ($LASTEXITCODE -ne 0) { throw "Windows rejected $LASTEXITCODE cursor(s)" }
}

# ---- Licenses and notice ---------------------------------------------------------------------------------
$licenses = [ordered]@{
    'GPL-3.0.txt'                 = 'raw\ful1e5__Bibata_Cursor\_LICENSE'
    'GPL-2.0.txt'                 = 'raw\catppuccin__cursors\_LICENSE'
    'LGPL-3.0.txt'                = 'raw\ful1e5__notwaita-cursor\_COPYING_LGPL'
    'CC-BY-SA-4.0.txt'            = 'raw\phisch__phinger-cursors\_LICENSE'
    'Apache-2.0 (AOSP NOTICE).txt' = 'work\Tech-Tac__aosp-cursors\aosp-cursors-windows-1.3.1\NOTICE.txt'
    'MIT (Modern Inverted).txt'   = 'raw\emvaized__modern_inverted_mouse_cursors\_LICENSE'
    'X11 (Hackneyed).txt'         = 'raw\Enthymeme__hackneyed-x11-cursors\LICENSE'
    'CC0-1.0 (Kenney).txt'        = 'work\kenney\kenney_cursor-pack\License.txt'
    'Capitaine COPYING.txt'       = 'raw\keeferrourke__capitaine-cursors\_COPYING'
    'Retrosmart COPYING.txt'      = 'raw\useless-anvil__retrosmart-cursor\_COPYING'
    'Notwaita COPYING.txt'        = 'raw\ful1e5__notwaita-cursor\_COPYING'
    'ComixCursors copyright.txt'  = 'work\comix-data\usr\share\doc\comixcursors-righthanded\copyright'
}
$licenseDir = Join-Path $staging 'LICENSES'
New-Item -ItemType Directory -Force $licenseDir | Out-Null
foreach ($name in $licenses.Keys) {
    $src = Join-Path $Sources $licenses[$name]
    if (-not (Test-Path $src)) { throw "license text missing: $src" }
    Copy-Item $src (Join-Path $licenseDir $name)
}

$categoryOrder = 'Minimal', 'macOS', 'Retro', 'Pixel & Gaming', 'Neon', 'Glass', 'Cute', 'Animated'
$rows = foreach ($dir in Get-ChildItem $staging -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'pack.ini') }) {
    $ini = @{}
    foreach ($line in Get-Content (Join-Path $dir.FullName 'pack.ini') -Encoding UTF8) {
        $eq = $line.IndexOf('=')
        if ($eq -gt 0) { $ini[$line.Substring(0, $eq)] = $line.Substring($eq + 1) }
    }
    [pscustomobject]@{
        Rank = [array]::IndexOf($categoryOrder, $ini['category']); Order = [int]$ini['order']
        Name = $ini['name']; Category = $ini['category']; Author = $ini['author']; License = $ini['license']
        Url = $ini['url']; Notes = $ini['notes']; Id = $dir.Name
    }
}
$notice = New-Object System.Text.StringBuilder
[void]$notice.AppendLine('# Bundled cursor packs')
[void]$notice.AppendLine()
[void]$notice.AppendLine('The cursor packs in this folder are separate works by their authors, redistributed unchanged in design under their own licenses. Full license texts are in `LICENSES/`. They are not part of the Cursors app code.')
[void]$notice.AppendLine()
[void]$notice.AppendLine('Changes made for bundling:')
[void]$notice.AppendLine()
[void]$notice.AppendLine('- Windows builds: files renamed to Windows role names; animated cursors keep only their 32, 48 and 64 px frames.')
[void]$notice.AppendLine('- Linux (Xcursor) themes and Kenney PNG icons: converted to .cur/.ani; missing roles reuse the pack''s own closest cursor.')
[void]$notice.AppendLine('- Neon packs: Bibata Modern Classic with the outline recolored and a glow added.')
[void]$notice.AppendLine()
[void]$notice.AppendLine('The conversion tool (`tools/PackTool`), the recipe (`tools/packs.recipe`) and the upstream source list (`tools/SOURCES.md`) are the corresponding source for these changes.')
[void]$notice.AppendLine()
[void]$notice.AppendLine('| Pack | Category | Author | License | Source | Notes |')
[void]$notice.AppendLine('| --- | --- | --- | --- | --- | --- |')
foreach ($r in $rows | Sort-Object Rank, Order, Name) {
    [void]$notice.AppendLine("| $($r.Name) | $($r.Category) | $($r.Author) | $($r.License) | $($r.Url) | $($r.Notes) |")
}
[IO.File]::WriteAllText((Join-Path $staging 'NOTICE.md'), $notice.ToString(), (New-Object System.Text.UTF8Encoding $false))

# ---- Publish ---------------------------------------------------------------------------------------------
$packs = Join-Path $root 'packs'
if (Test-Path $packs) { Remove-Item $packs -Recurse -Force }
Move-Item $staging $packs
$mb = [math]::Round((Get-ChildItem $packs -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Published $(@($rows).Count) packs to $packs ($mb MB). Report: $report\report.md"
