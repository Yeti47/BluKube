#!/usr/bin/env bash
# Regenerate the NSwag typed client from the server's OpenAPI document.
# Requires: server must be buildable (dotnet build src/BluKube.Server).
set -euo pipefail
cd "$(dirname "$0")/.."

echo "==> Generating OpenAPI document from BluKube.Server..."
dotnet build src/BluKube.Server --nologo -c Release

# The NSwag MSBuild target is wired in BluKube.Tui.csproj.
# Building the TUI project triggers regeneration.
echo "==> Regenerating BluKube.Tui/ApiClient/BluKubeClient.cs..."
dotnet build src/BluKube.Tui --nologo

echo "==> Done."