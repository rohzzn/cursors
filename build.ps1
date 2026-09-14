<#
  Builds bin\Cursors.exe: a single, dependency-free .NET Framework 4.8 executable
  (the runtime ships with Windows 10 and 11, so nothing needs to be installed to run it).

  Needs a Roslyn C# compiler, from Visual Studio or the VS Build Tools.
  Usage:  powershell -ExecutionPolicy Bypass -File build.ps1 [-Run] [-Package]
          -Package also writes dist\Cursors-<version>-windows.zip (exe + Packs + docs) and its SHA-256.
#>
param([switch]$Run, [switch]$Package)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

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

$csc = Find-Csc
if (-not $csc) { throw 'Roslyn csc.exe not found. Install "Visual Studio Build Tools" (MSBuild workload), or build Cursors.csproj with the .NET SDK.' }

$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path $fw)) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }

$icon = Join-Path $root 'Cursors.ico'
if (-not (Test-Path $icon)) { & (Join-Path $root 'tools\make-logo.ps1') }

$out = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

$refs = 'mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms',
        'System.IO.Compression', 'System.IO.Compression.FileSystem', 'Microsoft.VisualBasic' |
        ForEach-Object { "/r:$fw\$_.dll" }
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName

& $csc /nologo /noconfig /nostdlib+ /target:winexe /platform:anycpu /optimize+ /debug- /deterministic+ `
    /langversion:latest /warn:4 /nowarn:1591 `
    "/out:$out\Cursors.exe" "/win32manifest:$root\app.manifest" "/win32icon:$icon" `
    @refs @sources
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }

$size = [math]::Round((Get-Item "$out\Cursors.exe").Length / 1KB)
Write-Host "Built $out\Cursors.exe ($size KB)"

# The bundled library lives in packs\ (rebuilt by tools\build-packs.ps1). The exe looks for it in a Packs folder beside
# itself, so link bin\Packs to it instead of copying ~45 MB. Copying the bin folder elsewhere copies the real files.
$packs = Join-Path $root 'packs'
$link = Join-Path $out 'Packs'
if ((Test-Path $packs) -and -not (Test-Path $link)) {
    New-Item -ItemType Junction -Path $link -Target $packs | Out-Null
    Write-Host "Linked $link -> $packs"
}
# -Package: dist\Cursors-<version>-windows.zip with the exe, the library and the docs, plus its SHA-256.
if ($Package) {
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo("$out\Cursors.exe").ProductVersion
    if (-not $version) { throw 'Cursors.exe has no version (see src\AssemblyInfo.cs).' }
    if (-not (Test-Path (Join-Path $packs 'NOTICE.md'))) { throw 'packs\ is missing; run tools\build-packs.ps1 first.' }

    $dist = Join-Path $root 'dist'
    $name = "Cursors-$version"
    $stage = Join-Path $dist $name
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item "$out\Cursors.exe" $stage
    Copy-Item $packs (Join-Path $stage 'Packs') -Recurse
    foreach ($doc in 'LICENSE', 'README.md', 'CHANGELOG.md') {
        if (Test-Path (Join-Path $root $doc)) { Copy-Item (Join-Path $root $doc) $stage }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $dist "$name-windows.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding Ascii
    Write-Host "Packaged $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB), SHA-256 $hash"
}

if ($Run) { Start-Process "$out\Cursors.exe" }
