#!/usr/bin/env bash
# Run one Gemini implementation round for a task, isolated in its own git worktree.
#
#   scripts/gemini/run.sh <task-id>
#
# Input:  .tasks/<id>/spec.md          (required — written by the orchestrator)
#         .tasks/<id>/feedback.md      (optional — review notes for the next round)
# Output: worktree ../<repo>.worktrees/<id> on branch gemini/<id>, one commit per round,
#         .tasks/<id>/round-N/{prompt.md,gemini.json,gemini.err.log,verify.log,summary.md}
# Exit:   0 = Gemini finished AND verify.sh green; 1 otherwise. Re-running = next round.
#
# Env: GEMINI_MODEL (default: CLI's "auto"), WORKER_SANDBOX=1|0 (default 1, macOS seatbelt:
#      writes confined to the worktree), WORKER_TIMEOUT seconds (default 1800),
#      BASE=<ref> branch point for new tasks (default: current HEAD of main checkout).
source "$(dirname "$0")/_common.sh"

require_id "${1:-}"; task_paths "$1"
[ -f "$TASK/spec.md" ] || die "missing $TASK/spec.md"
[ -e "$TASK/running" ] && kill -0 "$(cat "$TASK/running")" 2>/dev/null && die "task $ID is already running (pid $(cat "$TASK/running"))"
load_api_key
gemini_cmd

# ── Worktree (created on first round, reused afterwards) ─────────────────────
if [ ! -d "$WT" ]; then
  git -C "$MAIN_REPO" rev-parse --verify -q HEAD >/dev/null || die "main repo has no commits yet — commit first"
  BASE_REF="$(git -C "$MAIN_REPO" rev-parse "${BASE:-HEAD}")"
  mkdir -p "$WT_ROOT"
  git -C "$MAIN_REPO" worktree add -q -b "$BR" "$WT" "$BASE_REF"
  echo "$BASE_REF" > "$TASK/base"
  # Restore up front so the sandboxed worker never needs to write outside the worktree.
  (cd "$WT" && dotnet tool restore -v q >/dev/null && dotnet restore -v q --nologo >/dev/null)
fi

ROUND=$(( $(cat "$TASK/round" 2>/dev/null || echo 0) + 1 ))
echo "$ROUND" > "$TASK/round"
OUT="$TASK/round-$ROUND"; mkdir -p "$OUT"
echo $$ > "$TASK/running"; trap 'rm -f "$TASK/running"' EXIT

# ── Prompt ───────────────────────────────────────────────────────────────────
{
  cat <<EOF
You are a Gemini implementation worker (task "$ID", round $ROUND). Your working directory is an
isolated git worktree. GEMINI.md (and CLAUDE.md it imports) describe this repo — follow them.

Hard rules:
- Implement exactly the task spec below. Stay within the files/areas it names; if something
  outside scope must change, make the minimal change and say so in your final summary.
- Do NOT run git commands that change state (commit, checkout, reset, rebase, stash, push).
- Do NOT edit CLAUDE.md, GEMINI.md, .claude/, .gemini/, scripts/, or existing migrations.
- Never read or print secrets (.env files, user-secrets, API keys).
- Before finishing, run scripts/verify.sh and iterate until it passes. If it can't pass,
  stop and explain exactly what fails and why.
- Finish with a short summary: files changed, what you did, deviations from spec, open questions.

==================== TASK SPEC ====================
EOF
  cat "$TASK/spec.md"
  if [ -f "$TASK/feedback.md" ]; then
    echo; echo "==================== REVIEW FEEDBACK (round $((ROUND-1))) — address all of it ===================="
    echo "The worktree already contains your previous round's work. Fix it in place."
    cat "$TASK/feedback.md"
    mv "$TASK/feedback.md" "$OUT/feedback-addressed.md"
  fi
} > "$OUT/prompt.md"

# ── Run Gemini ───────────────────────────────────────────────────────────────
ARGS=(--approval-mode yolo --skip-trust --output-format json)
[ -n "${GEMINI_MODEL:-}" ] && ARGS+=(-m "$GEMINI_MODEL")
if [ "${WORKER_SANDBOX:-1}" = "1" ]; then ARGS+=(--sandbox); export GEMINI_SANDBOX=sandbox-exec; fi

echo "▶ gemini round $ROUND for '$ID' in $WT (model: ${GEMINI_MODEL:-auto}, sandbox: ${WORKER_SANDBOX:-1})"
set +e
( cd "$WT" && \
  DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 \
  perl -e 'alarm shift; exec @ARGV' "${WORKER_TIMEOUT:-1800}" \
    "${GEMINI[@]}" "${ARGS[@]}" -p "Carry out the task described above." \
    < "$OUT/prompt.md" > "$OUT/gemini.json" 2> "$OUT/gemini.err.log" )
GEMINI_EXIT=$?

# ── Independent verification (never trust the worker's own claim) ───────────
( cd "$WT" && ./scripts/verify.sh ) > "$OUT/verify.log" 2>&1
VERIFY_EXIT=$?
set -e

# ── Snapshot the round as a commit on the worker branch (squashed on land) ──
git -C "$WT" add -A
# Unsigned on purpose: throwaway commits, squashed by land.sh (which respects your signing config).
git -C "$WT" diff --cached --quiet || git -C "$WT" -c commit.gpgsign=false commit -q --no-verify -m "gemini($ID): round $ROUND"

# ── Summary ─────────────────────────────────────────────────────────────────
RESPONSE="$(python3 - "$OUT/gemini.json" <<'PY'
import json, sys
try:
    d = json.load(open(sys.argv[1]))
except Exception as e:
    print(f"(no JSON output: {e})"); sys.exit()
if d.get("error"): print("ERROR:", json.dumps(d["error"])[:2000])
print((d.get("response") or "(empty response)").strip())
PY
)"
case $GEMINI_EXIT in 0) G="ok";; 142) G="TIMEOUT after ${WORKER_TIMEOUT:-1800}s";; 53) G="turn limit exceeded";; *) G="exit $GEMINI_EXIT";; esac
[ $VERIFY_EXIT -eq 0 ] && V="✅ green" || V="❌ red (see verify.log)"

{
  echo "# $ID — round $ROUND"
  echo "gemini: $G | verify: $V | branch: $BR | worktree: $WT"
  echo; echo "## Changed vs base"
  git -C "$MAIN_REPO" diff --stat "$(cat "$TASK/base")" "$BR" | tail -25
  echo; echo "## Worker summary"; echo "$RESPONSE"
  if [ $VERIFY_EXIT -ne 0 ]; then echo; echo "## verify.log (tail)"; tail -30 "$OUT/verify.log"; fi
  if [ $GEMINI_EXIT -ne 0 ]; then echo; echo "## gemini.err.log (tail)"; tail -20 "$OUT/gemini.err.log"; fi
} > "$OUT/summary.md"
cat "$OUT/summary.md"

[ $GEMINI_EXIT -eq 0 ] && [ $VERIFY_EXIT -eq 0 ]
