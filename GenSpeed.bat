@echo off
rem ============================================================
rem  Lanceur GenSpeed (interface sans fenetre console)
rem ============================================================
cd /d "%~dp0"

rem -- Theme sombre : installer ttkbootstrap si absent --
rem    (si l'install echoue, l'app retombe automatiquement sur le theme clair)
where python >nul 2>&1
if %errorlevel%==0 (
    python -c "import ttkbootstrap" >nul 2>&1 || python -m pip install --quiet --disable-pip-version-check ttkbootstrap
)

rem -- Lancement (pythonw = pas de console) --
where pythonw >nul 2>&1
if %errorlevel%==0 (
    start "" pythonw "main.py"
    goto :eof
)

where python >nul 2>&1
if %errorlevel%==0 (
    start "" python "main.py"
    goto :eof
)

echo Python est introuvable.
echo Installe-le depuis https://www.python.org/  ^(coche "Add Python to PATH"^)
pause
