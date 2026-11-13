# SomeMysteriousAppForJunction

Junction hackathon project — Blazor (.NET 10, Interactive Server) + EF Core / SQLite.

First time: `dotnet tool restore` (installs the pinned `dotnet-ef`).

```bash
scripts/dev.sh      # hot reload → http://localhost:5172
scripts/verify.sh   # build + tests (the green gate)
scripts/smoke.sh    # boot app, curl key routes
```

Conventions, layout and agent rules: see [CLAUDE.md](CLAUDE.md).
Claude Code skills: `/new-feature <Name> <what it does>`, `/ship`.
