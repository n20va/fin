#!/bin/bash
set -euo pipefail
PROJECT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if command -v dotnet >/dev/null 2>&1; then
    DOTNET_BIN="$(command -v dotnet)"
elif [[ -x /opt/homebrew/bin/dotnet ]]; then
    DOTNET_BIN=/opt/homebrew/bin/dotnet
else
    echo 'Не найден .NET SDK 10. Установите SDK и откройте Solution1.sln в Rider.' >&2
    exit 1
fi
exec "$DOTNET_BIN" run --project "$PROJECT_DIR/src/FinPlan/FinPlan.csproj" --no-launch-profile -- "$@"
