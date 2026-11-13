---
name: ship
description: Verify the working tree is green, then commit it as a small working increment. Use when the user says ship it, commit this, or checkpoint.
disable-model-invocation: true
---

# Ship a checkpoint

1. `git status` + `git diff --stat` — check for stray files (logs, `*.db`, secrets, `bin/`/`obj/`). Stop and ask if anything looks like a secret.
2. `scripts/verify.sh`. If red: fix if the cause is obvious and within the current task's scope; otherwise stop and report the failure output.
3. If routes, DI, or layout changed: `scripts/smoke.sh`.
4. Stage only files related to the work and commit with a concise imperative message (`feat: …`, `fix: …`, `chore: …`).
5. Only push if the user asked to. Never force-push.
6. Report: commit hash, one-line summary, test count.
