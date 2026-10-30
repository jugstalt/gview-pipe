@echo off
rem runtime state lives next to the version folders, so it survives updates
set PIPE_ROOT=%~dp0..
set Pipe__DataPath=%PIPE_ROOT%\data
set Pipe__FileRoots__files=%PIPE_ROOT%\files
set Pipe__Scripts__ScriptRoots__scripts=%PIPE_ROOT%\scripts
set Pipe__Scripts__RepositoriesPath=%PIPE_ROOT%\data\script-repos

cd /d "%~dp0pipe"
start dotnet gView.Pipe.Shell.dll --urls http://localhost:5100
