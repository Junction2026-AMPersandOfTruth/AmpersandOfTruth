# P2.12 Review Walk (`scripts/walk`)

One command walks the 10-step demo flow in a real Chromium/Chrome browser at **desktop (`1280×720`)** and **phone (`390×844`, `@2x` mobile touch)**, capturing full-page screenshots and generating `report.md` so reviewers can evaluate the UI without setting anything up locally.

## How to run

```bash
# Boot the app on a random port (Development), run the walk, and stop the server on exit:
scripts/walk.sh

# Run against an already-running local server:
scripts/walk.sh --url http://localhost:5172

# Write screenshots + report.md to a specific review folder:
scripts/walk.sh --out research/review-rounds/round-1

# Override the system browser binary (defaults to /usr/bin/google-chrome, fallback /usr/bin/chromium-browser):
CHROME=/usr/bin/chromium-browser scripts/walk.sh
```

By default, output is written to `research/review-rounds/<UTC yyyyMMdd-HHmm>/`:
- `report.md` — step × viewport table with screenshot links, timing (`s`), console errors / failed requests, and horizontal-overflow checks (`document.documentElement.scrollWidth > innerWidth`).
- `screens/NN-<step>-<desktop|phone>.png` — full-page PNGs (git-ignored via `research/review-rounds/*/screens/`).

Missing selectors or unbuilt routes never abort the walk: they log `step N skipped: <reason>` and continue.

## Round 1: fixture mode (judge flow, not findings)

Round 1 runs in **fixture mode** (`Investigations:Source = Fixtures`, the default in configuration):
- Stages 3–4 replay hand-written sample incidents rather than real pipeline output, and incident pages show the sample-data notice.
- **Reviewers in Round 1 judge the flow, hierarchy, responsiveness, states, and wording — not whether the sample historical findings are real pipeline discoveries.**
- Round 2 (after P2.11 states/mobile pass) verifies edge states, mobile polish, and every copy/layout fix from Round 1.

## Walk steps

Each step runs twice (`desktop` `1280×720`, then `phone` `390×844` with `deviceScaleFactor: 2, isMobile: true, hasTouch: true`):

1. **Landing `/`** — wait for Blazor circuit + `topic-input`.
2. **Topic chip** — click first `topic-chip`.
3. **Overview** — capture progress at `2 s` (`03-progress-*.png`), then wait up to `90 s` for `summary-header` + (`incident-card` or `empty-state`).
4. **Incident detail** — click first `incident-card`, wait for `incident-header` / `diff-viewer`.
5. **Diff toggle** — toggle `diff-view-toggle` (inline ↔ side by side on desktop; hidden `< 768 px` on phone by design).
6. **Article timeline** — follow first `/articles/...` link.
7. **Browser back** — return to previous page with state preserved in the URL.
8. **Search (`Mannerheim`)** — on `/`, type `Mannerheim` into `topic-input`, press `Enter`, wait for results.
9. **Out-of-scope / search (`climate change`)** — on `/`, type `climate change` into `topic-input`, press `Enter`, verify inline out-of-scope `EmptyState` or results.
10. **Methodology `/methodology`** — verify methodology document page renders.

## Reviewer checklist

### Copy rules (`docs/design/plan/3-states-and-trust.md`)

- [ ] **No overclaiming or intent:** no verbs of intent (“tried to deceive”, “tried to”, “deliberately”, “intentionally”, “in bad faith”), no account accusations (“propagandist”, “troll”, “sockpuppet”, “bad actor”), and no certainty claims (“proven”, “confirmed false”).
- [ ] **No pipeline jargon:** “revert”, “sha1”, “tier 2” (or “tier”), and “LLM” never appear in visible UI copy — use “removed”, “level”, and “automated assessment” instead.
- [ ] **No unexplained Finnish:** every Finnish string has an English neighbour or a *Machine translation* (`MtLabel`) toggle, and original Finnish remains one click away.
- [ ] **UTC on timestamps:** every timestamp includes `UTC` (e.g. `3 Mar 2014, 14:02 UTC`; dates `3 Mar 2014`); counts use thousands separators (`9,214 revisions`).
- [ ] **Attribution & evidence links:** every page carries the CC BY-SA 4.0 attribution footer linking to `/methodology`, and every quoted claim/edit links to its `fi.wikipedia.org` diff.

### Definition of done (`docs/design/plan/4-build-order.md`)

- [ ] **Fixture + pipeline flag:** works cleanly on fixtures, with the real pipeline behind `Investigations:Source`.
- [ ] **State matrix & tests:** every state in the matrix (loading, partial streaming, empty/out-of-scope, corpus/embedder/LLM unavailable, slow stage `> 20 s`, reconnect, failed stage) has a bUnit test, and every new route is in `AppSmokeTests`.
- [ ] **Test IDs match design:** `data-testid` attributes match `docs/design/plan/2-screens.md`.
- [ ] **Responsive at `1280×720` and `390 px`:** reads cleanly on both projector and phone with **zero horizontal scroll** (`scrollWidth <= innerWidth`).
- [ ] **Single source for levels & editors:** no level/tier wording outside `TierBadge`, and all editor usernames/IPs rendered via `EditorDisplay` / `EditorLabel` (IPs masked).
