#!/usr/bin/env bash
#
# One-command launcher for the Minecraft side project (Git Bash friendly).
#
#   ./run.sh            build, start the server in its own window, then the client
#   ./run.sh client     build and run the client only
#   ./run.sh server     build and run the server only (in this terminal)
#   ./run.sh debug      build, then open the workspace in VS Code for debugging
#
# Debugging from VS Code (Run and Debug):
#   "Client (Debug)"             client only
#   "Server (Debug)"             server only, in an integrated terminal so you can type commands
#   "Client + Server (Debug)"    both at once
# Breakpoints in src/Minecraft.Shared work in any of these sessions.

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

solution="Minecraft.sln"
client_project="src/Minecraft.Client/Minecraft.Client.csproj"
server_project="src/Minecraft.Server/Minecraft.Server.csproj"

usage() {
    cat <<'EOF'
Usage: ./run.sh [both|client|server|debug]

  both    Build, start the server in a separate window, then run the client (default)
  client  Build and run the client only
  server  Build and run the server only
  debug   Build, then open the workspace in VS Code for debugging
EOF
}

build() {
    echo "Building..."
    dotnet build "$solution" --nologo -v q
}

is_windows_shell() {
    case "${OSTYPE:-}" in
        msys* | cygwin* | win32*) return 0 ;;
        *) return 1 ;;
    esac
}

start_server_window() {
    if is_windows_shell; then
        # Git Bash on Windows: open a real console window so the server can read
        # commands. The doubled slash keeps MSYS from rewriting "/c" as a path.
        cmd.exe //c start "Minecraft Server" dotnet run --project "$server_project" --no-build
    elif command -v x-terminal-emulator >/dev/null 2>&1; then
        x-terminal-emulator -e dotnet run --project "$server_project" --no-build &
    elif command -v gnome-terminal >/dev/null 2>&1; then
        gnome-terminal -- dotnet run --project "$server_project" --no-build
    elif command -v osascript >/dev/null 2>&1; then
        osascript -e "tell application \"Terminal\" to do script \"cd '$(pwd)' && dotnet run --project '$server_project' --no-build\""
    else
        echo "No terminal launcher found; running the server in the background."
        dotnet run --project "$server_project" --no-build &
        server_pid=$!
        trap 'kill "$server_pid" 2>/dev/null || true' EXIT
    fi
}

run_both() {
    build
    echo "Starting the server in a separate window..."
    start_server_window
    sleep 1

    echo "Starting the client..."
    dotnet run --project "$client_project" --no-build

    echo
    echo 'Client closed. Type "quit" in the server window to stop the server.'
}

run_single() {
    build
    dotnet run --project "$1" --no-build
}

open_for_debugging() {
    build

    if command -v code >/dev/null 2>&1; then
        code --reuse-window .
    fi

    cat <<'EOF'
Pick "Client + Server (Debug)" in VS Code's Run and Debug panel and press F5.

The individual "Client (Debug)" and "Server (Debug)" configurations are there
too. Breakpoints in src/Minecraft.Shared are hit from either side.
EOF
}

case "${1:-both}" in
    both) run_both ;;
    client) run_single "$client_project" ;;
    server) run_single "$server_project" ;;
    debug) open_for_debugging ;;
    -h | --help | help) usage ;;
    *)
        usage >&2
        exit 1
        ;;
esac
