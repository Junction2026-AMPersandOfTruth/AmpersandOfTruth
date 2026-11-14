# Ampersand of Truth

**See what was pushed into Wikipedia, and what the community took out.** Live: https://asot.tech

Type a topic. The app finds the relevant Finnish Wikipedia articles on the history of Finland (19,742 articles, edits 2003–2026),
reads their edit history, and shows the insertions other editors removed: what was added, by which account, when, how long it
stayed live, and how sure we are that it matters. Every claim links to the exact revision on fi.wikipedia.org.

Junction hackathon project: Blazor (.NET 10, Interactive Server) + EF Core / SQLite for app state, MariaDB for the Wikipedia corpus,
bge-m3 (Ollama) for topic search, Gemini for assessments and translation.

## Run it

First time: `dotnet tool restore` (installs the pinned `dotnet-ef`).

```bash
scripts/dev.sh      # hot reload → http://localhost:5172
scripts/verify.sh   # build + tests (the green gate, works without the corpus)
scripts/smoke.sh    # boot app, curl key routes
scripts/walk.sh     # Playwright walk of the demo flow, desktop + phone screenshots
```

**With the real corpus** (articles, edit history, diffs):

```bash
scripts/corpus-tunnel.sh                                    # localhost:3307 → VPS MariaDB, :11435 → VPS bge-m3
dotnet user-secrets set "Corpus:Password" "<corpus_ro pw>" --project src/AmpersandOfTruth.Web   # ask a teammate
dotnet user-secrets set "Gemini:ApiKey" "<key>" --project src/AmpersandOfTruth.Web             # translations / assessments
```

Without the corpus the app still starts: pages show "not reachable" banners instead of data.

**Sample incidents vs the real pipeline:** until the pipeline (#39) is tuned, incidents are hand-written samples on real
revisions, and every page that shows them says so. Switch with `Investigations:Source` = `Fixtures` (default) or `Pipeline`.

Deploy to the VPS: `scripts/app-deploy.sh` (root ssh). Design system: [`docs/design/`](docs/design/README.md).
Conventions, layout and agent rules: [CLAUDE.md](CLAUDE.md). Claude Code skills: `/new-feature <Name> <what it does>`, `/ship`.
