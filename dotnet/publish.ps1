# Génère l'exe autonome distribuable (un seul fichier, runtime .NET embarqué).
# Lance simplement :  .\publish.ps1
# Le code source n'est pas affecté — relançable à volonté après chaque modif.

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet publish src/GenSpeed.App/GenSpeed.App.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none

$pub = "src/GenSpeed.App/bin/Release/net8.0-windows/win-x64/publish"
$exe = "$pub/GenSpeed.App.exe"
if (Test-Path $exe) {
    # Copie sous un nom distribuable plus propre.
    $nice = "$pub/GenSpeed.exe"
    Copy-Item $exe $nice -Force
    $size = "{0:N0} Mo" -f ((Get-Item $nice).Length / 1MB)
    Write-Host ""
    Write-Host "OK -> $((Resolve-Path $nice).Path)" -ForegroundColor Green
    Write-Host "Taille : $size  (autonome, aucun .NET a installer chez l'ami)" -ForegroundColor Green
}
