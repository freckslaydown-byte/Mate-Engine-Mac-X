#!/usr/bin/env bash
# update_macos.sh — pull latest, build, kill running app, and ditto the new app over.
#
# Runs on macOS (needs Unity 6000.4.8f1 + Xcode command line tools + ditto).
#
# Usage:
#   tools/update_macos.sh                 # pull+rebase feature branch, build, install
#   BRANCH=main tools/update_macos.sh     # different branch
#   tools/update_macos.sh --relaunch      # reopen the app after install
#   INSTALL_DIR=$HOME/Applications tools/update_macos.sh
#
# Env overrides: BRANCH, REMOTE, INSTALL_DIR, UNITY_BIN, KEEP_STASH
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_NAME="${APP_NAME:-MateEngineX}"
REMOTE="${REMOTE:-origin}"
BRANCH="${BRANCH:-$(git -C "$ROOT" branch --show-current)}"
BRANCH="${BRANCH:-feature/daemon-commands}"
INSTALL_DIR="${INSTALL_DIR:-/Applications}"
KEEP_STASH="${KEEP_STASH:-0}"
RELAUNCH="${RELAUNCH:-0}"

for arg in "$@"; do
  case "$arg" in
    --relaunch) RELAUNCH=1 ;;
    *) echo "unknown argument: $arg" >&2; exit 2 ;;
  esac
done

if [ "$(uname -s)" != "Darwin" ]; then
  echo "This script must run on macOS (Unity + ditto required)." >&2
  exit 1
fi

STASH_REF=""
restore_stash() {
  if [ -n "$STASH_REF" ] && [ "$KEEP_STASH" = "0" ]; then
    echo "[update] restoring stashed changes"
    git -C "$ROOT" stash pop "$STASH_REF" >/dev/null 2>&1 || true
  fi
}
trap restore_stash EXIT

# ── 1. Pull latest ────────────────────────────────────────────────────────────
echo "[update] repo: $ROOT"
echo "[update] pulling $REMOTE/$BRANCH"

git -C "$ROOT" fetch "$REMOTE"

if ! git -C "$ROOT" rev-parse --verify -q "refs/remotes/$REMOTE/$BRANCH" >/dev/null; then
  echo "[update] remote branch $BRANCH not found; using local branch as-is" >&2
else
  if [ -n "$(git -C "$ROOT" status --porcelain)" ]; then
    if [ "$KEEP_STASH" = "1" ]; then
      echo "[update] working tree dirty; stashing (KEEP_STASH=1, will not restore)" >&2
    else
      echo "[update] working tree dirty; stashing"
    fi
    STASH_REF="$(git -C "$ROOT" stash push -u -m "update_macos-$(date +%s)" | awk -F': ' '{print $NF}' | tail -1)"
  fi

  if ! git -C "$ROOT" rev-parse --verify -q "refs/heads/$BRANCH" >/dev/null; then
    git -C "$ROOT" checkout -b "$BRANCH" --track "$REMOTE/$BRANCH"
  else
    git -C "$ROOT" checkout "$BRANCH"
    git -C "$ROOT" pull --ff-only "$REMOTE" "$BRANCH"
  fi
  git -C "$ROOT" log --oneline -1
fi

# ── 2. Build ──────────────────────────────────────────────────────────────────
echo "[update] building..."
UNITY_BIN="${UNITY_BIN:-}" "$ROOT/tools/build_macos.sh"

APP_BUNDLE="$ROOT/Builds/macOS/$APP_NAME.app"
if [ ! -d "$APP_BUNDLE" ]; then
  echo "[update] build output not found: $APP_BUNDLE (check Builds/macos-build.log)" >&2
  exit 1
fi

# ── 3. Kill the running app ───────────────────────────────────────────────────
echo "[update] quitting $APP_NAME..."
osascript -e "tell application \"$APP_NAME\" to quit" >/dev/null 2>&1 || true
for _ in $(seq 1 30); do
  pgrep -x "$APP_NAME" >/dev/null 2>&1 || break
  sleep 0.5
done
if pgrep -x "$APP_NAME" >/dev/null 2>&1; then
  echo "[update] app did not quit within 15s; force killing" >&2
  killall -9 "$APP_NAME" >/dev/null 2>&1 || true
fi

# ── 4. Ditto the new app over ─────────────────────────────────────────────────
DEST="$INSTALL_DIR/$APP_NAME.app"
echo "[update] installing $APP_BUNDLE -> $DEST"
rm -rf "$DEST"
ditto "$APP_BUNDLE" "$DEST"
codesign --verify --deep --strict "$DEST"
echo "[update] installed & verified: $DEST"

if [ "$RELAUNCH" = "1" ]; then
  echo "[update] relaunching $APP_NAME"
  open "$DEST"
fi

echo "[update] done."