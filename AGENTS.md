# AGENTS.md (Codex and other non-Claude orchestrators)

`CLAUDE.md` is the source of truth for this repo. Read it first and follow it. Everything there applies to you too
(commands, layout, conventions, `scripts/verify.sh` before "done").

## Orchestrating Gemini workers

You orchestrate, Gemini implements. Follow `.claude/skills/gemini/SKILL.md` exactly: it's plain markdown. Wherever it
says "Claude", read it as "you". Differences for non-Claude agents:

- **Dispatch:** you have no background-task notifications. Run parallel workers (as many as you like) in one shell call and wait:
  `scripts/gemini/run.sh a > .tasks/a/run.log 2>&1 & scripts/gemini/run.sh b > .tasks/b/run.log 2>&1 & wait`
  (or run a single task in the foreground; it can take up to `WORKER_TIMEOUT`=1800s).
- **Linux:** the worker sandbox is macOS-only (`sandbox-exec`). On Linux, prefix with `WORKER_SANDBOX=0`.
- **Model:** `GEMINI_MODEL=<id>` picks the worker model (e.g. `gemini-4-carbon`); unset = Gemini CLI "auto".
- Workers write to `../<repo-folder>.worktrees/` (outside this repo) and need network, so your own sandbox must
  allow both. Codex: launch with
  `codex --sandbox workspace-write --add-dir "../$(basename "$PWD").worktrees" -c sandbox_workspace_write.network_access=true`.
- Commit trailers: use your own attribution, not Claude's.
