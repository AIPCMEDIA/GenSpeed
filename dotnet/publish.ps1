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

    # ── Paquet de distribution (dist/) : exe + LICENSE + LISEZMOI, zippes pour GitHub Releases. ──
    $ver  = "v2.3-beta"
    $dist = "dist"
    $stage = Join-Path $dist "GenSpeed-$ver"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item $nice (Join-Path $stage "GenSpeed.exe") -Force
    Copy-Item "../LICENSE" (Join-Path $stage "LICENSE.txt") -Force -ErrorAction SilentlyContinue
    @"
GenSpeed $ver
=============

Accelere le gameplay de C&C Generals: Zero Hour (et ses mods), meme en LAN.
Diagnostic de mismatch (desync) integre.

UTILISATION
-----------
1. Double-cliquez GenSpeed.exe (aucune installation, runtime .NET embarque).
2. Au 1er lancement, Windows SmartScreen peut afficher "editeur inconnu"
   (l'exe n'est pas signe) -> Informations complementaires -> Executer quand meme.
3. GenSpeed detecte automatiquement Steam + vos mods GenLauncher.

Gratuit, open-source (licence MIT). Aucune telemetrie, aucune connexion internet.
Code ecrit par IA, concu et dirige par l'auteur. Non affilie a Electronic Arts.
Projet : https://github.com/AIPCMEDIA/GenSpeed
"@ | Out-File (Join-Path $stage "LISEZMOI.txt") -Encoding utf8

    $zip = Join-Path $dist "GenSpeed-$ver.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$stage/*" -DestinationPath $zip -Force
    $zsize = "{0:N0} Mo" -f ((Get-Item $zip).Length / 1MB)
    Write-Host "ZIP -> $((Resolve-Path $zip).Path)  ($zsize)" -ForegroundColor Green
}
