@echo off
setlocal
cd /d "%~dp0"

if exist ".venv\Scripts\python.exe" goto run

where uv >nul 2>nul
if not errorlevel 1 goto create
echo Installing uv...
powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://astral.sh/uv/install.ps1 | iex"
if errorlevel 1 goto failed
set "PATH=%USERPROFILE%\.local\bin;%PATH%"

:create
rem A managed Python, because Windows can deny the Microsoft Store one camera access.
uv venv --python-preference only-managed --python 3.13 .venv
if errorlevel 1 goto failed
uv pip install --python .venv\Scripts\python.exe -r requirements.txt
if errorlevel 1 goto failed

:run
".venv\Scripts\python.exe" tracker.py --show %*
if errorlevel 1 pause
exit /b %errorlevel%

:failed
echo Setup failed.
if exist .venv rmdir /s /q .venv
pause
exit /b 1
