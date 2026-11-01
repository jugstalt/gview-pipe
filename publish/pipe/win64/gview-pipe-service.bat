@echo off
rem Runs gView.Pipe as a Windows service. Run it as administrator from the version folder:
rem   gview-pipe-service.bat install [name] [port]   create the service, or point it to this version
rem   gview-pipe-service.bat uninstall [name]        stop and delete the service
rem One root folder per instance (data, files, scripts, _config next to the version folders).
setlocal

set COMMAND=%~1
set NAME=%~2
set PORT=%~3
if "%PORT%"=="" set PORT=5100

set SVC=gview-pipe
set DISPLAY=gView.Pipe
if not "%NAME%"=="" (
    set SVC=gview-pipe-%NAME%
    set DISPLAY=gView.Pipe ^(%NAME%^)
)
set ACCOUNT=NT SERVICE\%SVC%

for %%I in ("%~dp0..") do set PIPE_ROOT=%%~fI
set APP=%~dp0pipe
set EXE=%APP%\gView.Pipe.Shell.exe

net session >nul 2>&1
if errorlevel 1 (
    echo Run this script as administrator.
    exit /b 1
)

if /i "%COMMAND%"=="install" goto install
if /i "%COMMAND%"=="uninstall" goto uninstall
echo Usage: %~nx0 install [name] [port]
echo        %~nx0 uninstall [name]
exit /b 1

:install
if not exist "%EXE%" (
    echo %EXE% not found.
    exit /b 1
)

sc query %SVC% >nul 2>&1
if errorlevel 1 goto create

rem the service exists: only update it if it belongs to this root folder
rem BINARY_PATH_NAME : "<root>\<version>\pipe\gView.Pipe.Shell.exe" --urls ...
set OLDROOT=
for /f "tokens=1,* delims=:" %%a in ('sc qc %SVC% ^| findstr /c:"BINARY_PATH_NAME"') do (
    for %%p in (%%b) do if not defined OLDROOT for %%r in ("%%~dpp..\..") do set OLDROOT=%%~fr
)
if /i not "%OLDROOT%"=="%PIPE_ROOT%" (
    echo The service %SVC% belongs to another folder: %OLDROOT%
    echo Use another name or uninstall it first.
    exit /b 1
)
call :stop || exit /b 1
sc config %SVC% binPath= "\"%EXE%\" --urls http://localhost:%PORT%" start= delayed-auto DisplayName= "%DISPLAY%" >nul || exit /b 1
goto configure

:create
sc create %SVC% binPath= "\"%EXE%\" --urls http://localhost:%PORT%" start= delayed-auto DisplayName= "%DISPLAY%" obj= "%ACCOUNT%" >nul || exit /b 1

:configure
sc description %SVC% "gView.Pipe workflows (%PIPE_ROOT%)" >nul
sc failure %SVC% reset= 86400 actions= restart/60000/restart/60000/restart/60000 >nul

rem runtime state lives next to the version folders, so it survives updates (as in gview-pipe.bat)
reg add "HKLM\SYSTEM\CurrentControlSet\Services\%SVC%" /v Environment /t REG_MULTI_SZ /f /s ";" /d "PIPE_CONFIG_PATH=%PIPE_ROOT%\_config;Pipe__DataPath=%PIPE_ROOT%\data;Pipe__FileRoots__files=%PIPE_ROOT%\files;Pipe__Scripts__ScriptRoots__scripts=%PIPE_ROOT%\scripts;Pipe__Scripts__RepositoriesPath=%PIPE_ROOT%\data\script-repos" >nul || exit /b 1

for %%D in (data files scripts _config) do if not exist "%PIPE_ROOT%\%%D" mkdir "%PIPE_ROOT%\%%D"

rem the service account may only write where the app keeps its state
for %%D in ("%PIPE_ROOT%\data" "%PIPE_ROOT%\files" "%PIPE_ROOT%\scripts" "%PIPE_ROOT%\_config") do (
    icacls %%D /grant "%ACCOUNT%:(OI)(CI)M" >nul || exit /b 1
)

rem the event log source must exist before the service account writes to it
powershell -NoProfile -Command "if (-not [Diagnostics.EventLog]::SourceExists('gView.Pipe')) { New-EventLog -LogName Application -Source 'gView.Pipe' }" >nul

sc start %SVC% >nul || exit /b 1
echo Service %SVC% runs %EXE% on http://localhost:%PORT% as %ACCOUNT%.
exit /b 0

:uninstall
sc query %SVC% >nul 2>&1
if errorlevel 1 (
    echo The service %SVC% does not exist.
    exit /b 1
)
call :stop || exit /b 1
sc delete %SVC% >nul || exit /b 1
echo Service %SVC% deleted. The folders data, files and scripts are kept.
exit /b 0

:stop
sc stop %SVC% >nul 2>&1
for /l %%i in (1,1,30) do (
    sc query %SVC% | find "STOPPED" >nul && exit /b 0
    timeout /t 1 /nobreak >nul
)
echo The service %SVC% did not stop.
exit /b 1
