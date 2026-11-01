@echo off
rem runtime state lives next to the version folders, so it survives updates
set PIPE_ROOT=%~dp0..
set Pipe__DataPath=%PIPE_ROOT%\data
set Pipe__FileRoots__files=%PIPE_ROOT%\files
set Pipe__Scripts__ScriptRoots__scripts=%PIPE_ROOT%\scripts
set Pipe__Scripts__RepositoriesPath=%PIPE_ROOT%\data\script-repos

rem pipe.config next to the version folders, created from the prototype on first start;
rem default address; "urls" in pipe.config wins, so another instance sets its port there
set PIPE_CONFIG_PATH=%PIPE_ROOT%\_config
set ASPNETCORE_URLS=http://localhost:5100

cd /d "%~dp0pipe"
start dotnet gView.Pipe.Shell.dll
