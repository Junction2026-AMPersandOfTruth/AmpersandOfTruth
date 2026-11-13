#!/usr/bin/env bash
# Throw away a Gemini task's worktree and branch (task files kept under .tasks/_discarded).
#   scripts/gemini/discard.sh <task-id>
source "$(dirname "$0")/_common.sh"

require_id "${1:-}"; task_paths "$1"
[ -e "$TASK/running" ] && kill -0 "$(cat "$TASK/running")" 2>/dev/null && die "task $ID is still running"
[ -d "$WT" ] && git -C "$MAIN_REPO" worktree remove --force "$WT"
git -C "$MAIN_REPO" show-ref -q --verify "refs/heads/$BR" && git -C "$MAIN_REPO" branch -q -D "$BR"
[ -d "$TASK" ] && mkdir -p "$TASKS_DIR/_discarded" && mv "$TASK" "$TASKS_DIR/_discarded/$ID-$(date +%H%M%S)"
echo "🗑  discarded $ID"
