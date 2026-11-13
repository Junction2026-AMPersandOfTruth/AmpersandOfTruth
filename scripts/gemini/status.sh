#!/usr/bin/env bash
# One line per active Gemini task.   scripts/gemini/status.sh
source "$(dirname "$0")/_common.sh"

shopt -s nullglob
printf "%-28s %-6s %-9s %-8s %s\n" TASK ROUND STATE VERIFY "FILES CHANGED"
for dir in "$TASKS_DIR"/*/; do
  ID="$(basename "$dir")"; [[ "$ID" == _* ]] && continue
  task_paths "$ID"
  ROUND="$(cat "$TASK/round" 2>/dev/null || echo 0)"
  if [ -e "$TASK/running" ] && kill -0 "$(cat "$TASK/running")" 2>/dev/null; then STATE=running
  elif [ "$ROUND" = 0 ]; then STATE=spec-only
  else STATE=done; fi
  VERIFY="-"
  if [ "$STATE" = done ] && [ -f "$TASK/round-$ROUND/verify.log" ]; then
    grep -q "verify passed" "$TASK/round-$ROUND/verify.log" && VERIFY=green || VERIFY=RED
  fi
  FILES="-"
  [ -f "$TASK/base" ] && FILES="$(git -C "$MAIN_REPO" diff --name-only "$(cat "$TASK/base")" "$BR" 2>/dev/null | wc -l | tr -d ' ')"
  printf "%-28s %-6s %-9s %-8s %s\n" "$ID" "$ROUND" "$STATE" "$VERIFY" "$FILES"
done
