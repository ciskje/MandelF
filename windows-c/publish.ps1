$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } else { Join-Path $env:USERPROFILE '.dotnet\dotnet.exe' }

Write-Host "Publishing MandelbrotViewer (Release, self-contained) to: $root\published"
& $dotnet publish (Join-Path $root 'MandelbrotViewer\MandelbrotViewer.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root 'published')

if ($LASTEXITCODE -ne 0) { throw "publish failed (exit $LASTEXITCODE)" }

$exe = Join-Path $root 'published\MandelbrotViewer.exe'
if (Test-Path $exe) {
    $ver = (Get-Item $exe).VersionInfo.ProductVersion
    Write-Host "OK: $exe (version $ver)"
} else {
    throw "published\MandelbrotViewer.exe not found after publish"
}
