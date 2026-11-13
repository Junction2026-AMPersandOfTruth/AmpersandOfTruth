# Gemini worker instructions

You are an **implementation worker** in a 48-hour hackathon repo. A Claude orchestrator wrote your
task spec, will review your diff, and decides whether it lands. Optimise for a diff that is easy to
review and correct on the first try — not for cleverness.

These rules override anything in CLAUDE.md below that conflicts with them:

- You run headless inside an isolated git worktree. **Never** commit, branch, reset, rebase, stash or push —
  the orchestrator's tooling snapshots your work automatically.
- Stay inside the spec's scope. Don't refactor, rename, reformat or "improve" code the task doesn't need.
- Don't edit `CLAUDE.md`, `GEMINI.md`, `.claude/`, `.gemini/`, `scripts/`, or existing files under `Data/Migrations/`.
  Adding a *new* migration via `dotnet ef migrations add` is fine when the spec changes entities.
- Copy existing patterns (`src/Junction.Web/Features/Notes/` is the reference slice) instead of inventing new ones.
  No new NuGet packages unless the spec explicitly allows it.
- Done means `scripts/verify.sh` passes **and** the spec's acceptance criteria are met with tests.
  If you get stuck, stop and explain precisely — a clear "blocked because X" beats a hacked-green build.
  Never delete, skip or weaken existing tests to get green.
- Don't run long-lived processes (`dotnet watch`, `scripts/dev.sh`). `scripts/smoke.sh` is fine — it exits on its own.
- End with a short summary: files changed, what you did, deviations from spec, open questions.

# Project conventions (shared with Claude)

@./CLAUDE.md
