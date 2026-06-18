#!/usr/bin/env bash
# Build the single self-contained Linux binary -> dist/linuxsimplify-pmx
set -e
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
cd "$(dirname "$0")"

DOTNET="$(command -v dotnet || true)"
[ -z "$DOTNET" ] && [ -x "$HOME/.dotnet/dotnet" ] && DOTNET="$HOME/.dotnet/dotnet"
if [ -z "$DOTNET" ]; then
    echo "Need the .NET 8 SDK to build. Get it:"
    echo "  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0"
    exit 1
fi

"$DOTNET" publish -c Release -r linux-x64 --self-contained true -o dist
chmod +x dist/linuxsimplify-pmx
echo
echo "Built: dist/linuxsimplify-pmx  ($(du -h dist/linuxsimplify-pmx | cut -f1))"
echo "Ship that one file. Users: chmod +x linuxsimplify-pmx && ./linuxsimplify-pmx"
