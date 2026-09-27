#!/usr/bin/env bash
# Fast, game-free feedback loop for production changes. Run from any directory.
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
export DOTNET_ROLL_FORWARD=Major

dotnet build NearbyCraft.csproj -c Debug --no-restore
dotnet run --project tests/SchedulerTests.csproj --no-restore
dotnet run --project tests/MachineTransactions.csproj --no-restore
dotnet run --project tests/WorkshopStoreTests.csproj --no-restore

if [[ "${1:-}" == "--full" ]]; then
    dotnet run --project tests/TransferTests.csproj --no-restore
    dotnet run --project tests/PatchSites.csproj --no-restore
    python3 tools/verify_package.py
    python3 -m unittest discover -s tests -p 'test_*.py' -v
    dotnet build NearbyCraft.csproj -c Debug -p:GameplayQA=true --no-restore
fi
