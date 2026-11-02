#!/usr/bin/env bash
# Fast checks on exactly what is about to be pushed: a clean checkout of HEAD, not the working tree.
# Catches what local runs miss: files that were never committed (they exist only on this machine)
# and tests or code left behind in the working tree. Runs as the pre-push hook
# (git config core.hooksPath .githooks). Full Docker/e2e coverage stays in scripts/verify-phase4.sh.
#
#   bash scripts/preflight.sh            # what the hook runs
#   git push --no-verify                 # skip once, e.g. for a docs-only push
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
watched=(frontend backend tests scripts .github compose.yaml compose.tunnel.yaml compose.production.yaml package.json package-lock.json Gomoku.sln)

dirty="$(git status --porcelain --untracked-files=all -- "${watched[@]}")"
if [[ -n "$dirty" && "${PREFLIGHT_ALLOW_DIRTY:-0}" != 1 ]]; then
  echo "✘ These files are not committed, so they will NOT reach GitHub (CI would build without them):" >&2
  echo "$dirty" >&2
  echo "  Commit them (or remove them) and push again. Skip this check once with: git push --no-verify" >&2
  exit 1
fi

work="$(mktemp -d)"
cleanup() { git -C "$root" worktree remove --force "$work" >/dev/null 2>&1 || rm -rf "$work"; }
trap cleanup EXIT
git worktree add --detach --quiet "$work" HEAD
# Reuse installed packages; everything else comes from the commit itself.
ln -s "$root/node_modules" "$work/node_modules"
[[ -d "$root/frontend/node_modules" ]] && ln -s "$root/frontend/node_modules" "$work/frontend/node_modules"
cd "$work"

step() {
  local name="$1"; shift
  local log="$work/.preflight-$(echo "$name" | tr ' /' '__').log"
  printf '▶ %-34s' "$name"
  local start=$SECONDS
  if "$@" >"$log" 2>&1; then
    printf '✓ %ss\n' $((SECONDS - start))
  else
    printf '✘\n'
    echo "----- $name: last 60 lines -----" >&2
    tail -n 60 "$log" | sed -E 's/\x1b\[[0-9;]*m//g' >&2
    exit 1
  fi
}

echo "Preflight on $(git rev-parse --short HEAD) (clean checkout)"
step "frontend typecheck" npm run typecheck -w frontend
step "frontend lint" npm run lint -w frontend
step "frontend unit tests" npm run test -w frontend -- --run
step "frontend build" npm run build -w frontend
step ".NET build" dotnet build Gomoku.sln -c Release
for project in Core AI Application ModelEvaluation SelfPlay; do   # no database needed
  step ".NET $project tests" dotnet test "tests/Gomoku.$project.Tests" -c Release --no-build
done
echo "✓ Preflight passed. API/Infrastructure tests (database) and e2e run in CI and scripts/verify-phase4.sh."
