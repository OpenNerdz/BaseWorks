#!/usr/bin/env bash
# Main-menu-only native asset smoke test. Restores the installed mod on every exit.
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
game_dir='/home/brad/.local/share/Steam/steamapps/common/7 Days To Die'
installed="$game_dir/Mods/NearbyCraft"
release="$repo_dir/dist/NearbyCraft-2.0.6-V3.2.zip"
log="$repo_dir/dist/asset-qa-native.log"
test_dir="$(mktemp -d "$repo_dir/dist/asset-qa.XXXXXX")"

if [[ ! -d "$installed" || ! -f "$release" || ! -f "$repo_dir/bin/Debug/net48/NearbyCraft.dll" ]]; then
    echo 'Installed mod, release archive, or opt-in QA DLL is missing.' >&2
    exit 1
fi
if pgrep -x '7DaysToDie.exe' >/dev/null; then
    echo 'Game is running; refusing to replace its mod files.' >&2
    exit 1
fi

unzip -q "$release" -d "$test_dir/staging"
cp "$repo_dir/bin/Debug/net48/NearbyCraft.dll" "$test_dir/staging/NearbyCraft/NearbyCraft.dll"

restore() {
    if [[ -d "$test_dir/original" ]]; then
        if pgrep -x '7DaysToDie.exe' >/dev/null; then
            echo "Game is still running; original mod remains safe at $test_dir/original. Refusing a live-file swap." >&2
            return
        fi
        if [[ -d "$installed" ]]; then mv "$installed" "$test_dir/tested"; fi
        mv "$test_dir/original" "$installed"
        echo "RESTORED installed NearbyCraft; test build retained at $test_dir/tested"
    fi
}
stop_test_game() {
    for ((shutdown=0; shutdown<20; shutdown++)); do
        if ! pgrep -x '7DaysToDie.exe' >/dev/null; then return 0; fi
        sleep 1
    done
    local pid
    for pid in $(pgrep -x '7DaysToDie.exe' || true); do
        if ps -p "$pid" -o args= | rg -q -- '-NearbyCraftAssetQA'; then
            kill -TERM "$pid" || true
        fi
    done
    for ((shutdown=0; shutdown<15; shutdown++)); do
        if ! pgrep -x '7DaysToDie.exe' >/dev/null; then return 0; fi
        sleep 1
    done
    echo 'QA game did not exit. Test build is left installed until the game is closed; original is in the printed backup directory.' >&2
    return 1
}
trap restore EXIT INT TERM
mv "$installed" "$test_dir/original"
mv "$test_dir/staging/NearbyCraft" "$installed"
echo "TESTING six custom models; original mod safely held at $test_dir/original"
if [[ -f "$log" ]]; then mv "$log" "$test_dir/previous.log"; fi

steam -applaunch 251570 \
    "-UserDataFolder=Z:/home/brad/Desktop/7 days mod/NearbyCraft/qa-userdata" \
    -NearbyCraftAssetQA -screen-width 1280 -screen-height 720 -screen-fullscreen 0 \
    -noeac -nogs -force-d3d11 -logfile "Z:/home/brad/Desktop/7 days mod/NearbyCraft/dist/asset-qa-native.log"

for ((attempt=0; attempt<120; attempt++)); do
    if [[ -f "$log" ]] && rg -q '\[NearbyCraft AssetQA\] COMPLETE' "$log"; then
        echo "PASS: native asset smoke test; see $log"
        stop_test_game
        exit 0
    fi
    if [[ -f "$log" ]] && rg -q '\[NearbyCraft AssetQA\] FAILED' "$log"; then
        echo "FAIL: native asset smoke test; see $log" >&2
        stop_test_game
        exit 1
    fi
    sleep 2
done
echo "TIMEOUT: native asset smoke test; see $log" >&2
stop_test_game
exit 1
