---
name: gemini
description: Delegate well-specified implementation work to Gemini workers running headless in isolated git worktrees, then review and land their diffs. Use when the user asks to use Gemini, to parallelize implementation, or to hand off a clearly-scoped feature/bugfix/test-writing task.
---

# Orchestrating Gemini workers

Division of labour: **you** (Claude) own decomposition, specs, contracts, review, integration, and anything
architectural or ambiguous. **Gemini** does the typing: implementing a clearly-specified slice, writing
tests, mechanical refactors within one feature. If you can't write a crisp spec, don't delegate — do it yourself.

Tooling lives in `scripts/gemini/`. Task state lives in `.tasks/<id>/` (git-ignored). Workers run in
`../<repo>.worktrees/<id>` on branch `gemini/<id>`.

## 1. Prepare
- Worktrees branch from the **committed** HEAD. Commit (or ask the user to let you commit) anything the
  worker needs to see first — including shared contracts you wrote yourself (entities, interfaces, nav slots).
- Split work so parallel tasks touch **disjoint files**. Expected shared-file touches (Program.cs line,
  NavMenu block, AppSmokeTests InlineData) rebase cleanly; two tasks adding migrations do NOT —
  serialize tasks that change the EF model, or have only one of them add the migration.
- Task id: kebab-case, e.g. `leaderboard-page`.

## 2. Write the spec
Copy `scripts/gemini/TASK_TEMPLATE.md` to `.tasks/<id>/spec.md` and fill it in. What makes Gemini succeed:
explicit file scope, exact contracts (signatures, routes, `data-testid`s, entity fields), a pointer to the
file to imitate, and acceptance criteria that each map to a test. Keep it under ~80 lines.

## 3. Dispatch
`scripts/gemini/run.sh <id>` — run it with the Bash tool's `run_in_background: true`; you're notified on
exit. Several tasks may run in parallel (one background call each), but the weekend key is rate-limited:
**max 3 concurrent** unless the user says otherwise. While they run, do your own work — don't poll.
`scripts/gemini/status.sh` shows all tasks. Optional env: `GEMINI_MODEL=<name>`, `WORKER_TIMEOUT=<sec>`.

## 4. Review — never land unread
Exit 0 means Gemini finished *and* verify.sh is green; that is necessary, not sufficient. Read
`.tasks/<id>/round-N/summary.md`, then the diff itself: `git diff $(cat .tasks/<id>/base) gemini/<id>`.
Check:
- Scope: only the files the spec allowed; no drive-by edits, no edits to CLAUDE.md/scripts/.claude.
- Contracts match the spec exactly (names, routes, testids, signatures).
- Tests actually assert the acceptance criteria (not tautologies; no deleted/skipped tests).
- Conventions from CLAUDE.md: DbContext factory per operation, `AsNoTracking`, `InvokeAsync(StateHasChanged)`,
  UTC `TimeProvider`, no secrets, no new packages unless allowed.
- No migration edits; a new migration exists iff the model changed.

## 5. Decide
- **Good** → `scripts/gemini/land.sh <id> "feat: <summary>"` (rebases on current branch, re-verifies,
  squash-merges, cleans up). Append the attribution trailer lines to the message as for any commit you make.
  Exit 2 = rebase conflict → resolve in the worktree yourself if trivial, or send feedback.
- **Fixable** → write concrete, numbered issues to `.tasks/<id>/feedback.md` (file + what's wrong + what you
  want instead), re-run `run.sh <id>`. It continues in the same worktree.
- **Small nit** → faster to fix it yourself in the worktree, commit there, then land.
- **After 2 failed feedback rounds**, or if the worker misunderstood the task → `scripts/gemini/discard.sh <id>`
  and either rewrite the spec or implement it yourself. Don't loop.

## 6. Report to the user
Per task: landed / in review / discarded, commit hash, and anything you changed or doubted in review.
