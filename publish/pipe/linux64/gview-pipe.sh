#!/bin/bash

# runtime state lives next to the version folders, so it survives updates
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PIPE_ROOT="$(dirname "$SCRIPT_DIR")"
export Pipe__DataPath="$PIPE_ROOT/data"
export Pipe__FileRoots__files="$PIPE_ROOT/files"
export Pipe__Scripts__ScriptRoots__scripts="$PIPE_ROOT/scripts"
export Pipe__Scripts__RepositoriesPath="$PIPE_ROOT/data/script-repos"

# pipe.config next to the version folders, created from the prototype on first start;
# default address; "urls" in pipe.config wins, so another instance sets its port there
export PIPE_CONFIG_PATH="$PIPE_ROOT/_config"
export ASPNETCORE_URLS="http://localhost:5100"

cd "$SCRIPT_DIR/pipe"

dotnet gView.Pipe.Shell.dll &
