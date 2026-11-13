#!/usr/bin/env bash
# Land a reviewed Gemini task: rebase onto the current branch, re-verify, squash-merge, clean up.
#
#   scripts/gemini/land.sh <task-id> "<commit message>"
#
# Exit 2 = rebase conflict (worktree left untouched for you to resolve or re-run with feedback).
source "$(dirname "$0")/_common.sh"

require_id "${1:-}"; task_paths "$1"
MSG="${2:-}"; [ -n "$MSG" ] || die "usage: land.sh <task-id> \"<commit message>\""
[ -d "$WT" ] || die "no worktree for $ID"
[ -e "$TASK/running" ] && die "task $ID is still running"
[ -z "$(git -C "$MAIN_REPO" status --porcelain --untracked-files=no)" ] || die "main checkout has uncommitted changes — commit or stash first"

TARGET="$(git -C "$MAIN_REPO" branch --show-current)"
echo "▶ rebasing $BR onto $TARGET"
if ! git -C "$WT" rebase -q "$TARGET"; then
  git -C "$WT" rebase --abort
  echo "❌ conflict rebasing onto $TARGET. Options: resolve manually in $WT, or write .tasks/$ID/feedback.md and re-run." >&2
  exit 2
fi
git -C "$MAIN_REPO" rev-parse "$TARGET" > "$TASK/base"   # diffs/status now relative to the new base

echo "▶ verifying rebased result"
( cd "$WT" && ./scripts/verify.sh ) || die "verify failed after rebase — not landing"

git -C "$MAIN_REPO" merge -q --squash "$BR" >/dev/null
if git -C "$MAIN_REPO" diff --cached --quiet; then
  echo "ℹ️  $ID has no net changes vs $TARGET — nothing to commit, cleaning up"
else
  git -C "$MAIN_REPO" commit -q -m "$MSG"
  echo "✅ landed $ID as $(git -C "$MAIN_REPO" rev-parse --short HEAD) on $TARGET"
fi

git -C "$MAIN_REPO" worktree remove --force "$WT"
git -C "$MAIN_REPO" branch -q -D "$BR"
mkdir -p "$TASKS_DIR/_done" && mv "$TASK" "$TASKS_DIR/_done/$ID-$(date +%H%M%S)"
