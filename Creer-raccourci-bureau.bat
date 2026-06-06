@echo off
rem ============================================================
rem  Cree un raccourci "GenSpeed" sur le Bureau
rem ============================================================
setlocal
set "DIR=%~dp0"

rem -- Trouver pythonw (sans console), sinon python --
set "PYW="
for /f "delims=" %%i in ('where pythonw 2^>nul') do if not defined PYW set "PYW=%%i"
if not defined PYW for /f "delims=" %%i in ('where python 2^>nul') do if not defined PYW set "PYW=%%i"

if not defined PYW (
    echo Python introuvable. Installe Python puis relance ce fichier.
    echo https://www.python.org/  ^(coche "Add Python to PATH"^)
    pause
    exit /b 1
)

set "ICON=%DIR%png\genspeed.ico"
if not exist "%ICON%" set "ICON=%PYW%,0"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws=New-Object -ComObject WScript.Shell; $d=[Environment]::GetFolderPath('Desktop'); $lnk=$ws.CreateShortcut($d+'\GenSpeed.lnk'); $lnk.TargetPath='%PYW%'; $lnk.Arguments=([char]34+'%DIR%main.py'+[char]34); $lnk.WorkingDirectory='%DIR%'; $lnk.IconLocation='%ICON%'; $lnk.Description='GenSpeed - vitesse de jeu pour Generals Zero Hour'; $lnk.Save()"

if %errorlevel%==0 (
    echo.
    echo Raccourci "GenSpeed" cree sur le Bureau.
) else (
    echo.
    echo Echec de la creation du raccourci.
)
pause
