# Ampersand of Truth

**See what was pushed into Wikipedia, and what the community took out.**

Ampersand of Truth takes a topic, finds the relevant Finnish Wikipedia articles on the history of Finland, mines 20 years of their edit history, and shows the insertions that editors rejected: what was added, by which account, when, how long it stayed live, and how sure we are that it matters.

This system is the **UI plan for epic P2 (issue #4)**. It contains the flow, the routes and the screens (sections after this one), the tokens and the component inventory, and live previews of every shared component and of the three key pages. It is written for the people and agents building the Blazor Server app. Everything shown is **sample data** (a made-up Winter War incident), not pipeline output.

> **The test (from #4):** someone who has never seen the app types a topic and, with no explanation, understands *what was attempted on Wikipedia, why we flagged it, and how sure we are.* Target: under 60 s and at most 3 clicks from landing to “I understand this incident”.

---

## 1 · Principles

1. **Evidence first.** Every claim on screen is one click from the Wikipedia revision that backs it. Every incident card, event row and quoted span links out to `fi.wikipedia.org/?diff=<rev>&oldid=<parent>`.
2. **Show the work while it happens.** Articles appear as soon as retrieval returns, and incidents stream in as they are scored. The live progress view is part of the explanation, not a spinner.
3. **English first, Finnish always one click away.** Translated text always carries a *Machine translation* tag and the original under it or behind a toggle. We never present translated text as if it were the Wikipedia content.
4. **Say only what the edit history shows.** It shows what was written, by which account, when, and that it was undone. It does not show intent. The wording rules below follow P0.7 (#18).
5. **Quiet throughout.** The app reports. It does not sound alarms. All three tiers are pale tints with dark text, told apart by the 1–3 pips and the wording. The one saturated colour is the link blue. Red appears only on system errors, so red never means guilty.

## 2 · Voice and vocabulary

Serious and investigative, not alarmist. We describe, we don't accuse.

| Use | Don't use |
|---|---|
| “Removed by Wikipedia editors” (tier 1) | “fake”, “lie”, “debunked” |
| “Disputed and removed” (tier 2) | “proven false”, “misinformation” as a verdict |
| “Pattern consistent with a coordinated attempt” (tier 3) | “propaganda campaign”, “troll farm” |
| “3 accounts inserted this claim” | “propagandist”, “bad actor”, “disinformation agent” |
| “Automated assessment”, with the quoted evidence beside it | “AI detected”, “our AI knows” |
| “live for 61 days in total” | “spread for 61 days” |
| “No disputed insertions found for this topic” plus what we checked | “Clean!”, “No propaganda here” |

**Numbers and time.** Dates are `3 Mar 2014`, and timestamps are `3 Mar 2014, 14:02 UTC` (the data is UTC, so we always show the zone). Durations are “61 days”, “2 years 3 months”. Counts use thousands separators (`9,214 revisions`) in the `t-data` mono style wherever they sit in a row of figures.

**Editor display (P0.7 recommendation).** Account names appear exactly as on Wikipedia and link to their public contributions page. IP addresses are partially masked (`85.76.x.x`). We never show a per-editor score, and we describe accounts only by observable facts (“12 edits, 9 reverted, all on one article”). This is implemented once, in `EditorLabel`.

## 3 · Visual foundations

**Look and feel: Wikipedia-inspired.** The interface borrows the feel of Wikipedia's own interface (Vector 2022 skin, Codex components), so the evidence feels at home next to the articles it comes from:
- near-square 2-px corners on everything
- a white page
- grey *infobox* panels (`surface-sunken`) for the side columns, toolbars and the stage tracker
- 1-px grey hairlines (`line`) instead of shadows
- serif page and section titles with a rule under them (`t-h1`, `t-h2`)
- system sans body text
- link blue (`accent` #3366cc), with links underlined only on hover
- bold, square buttons, with the primary one filled blue
- a `wikitable`-style grid for revision lists

It is **inspired by, not branded as** Wikipedia. We use our own wordmark, no puzzle globe and our own tier colours, and every Wikipedia quote carries a link to its revision. A viewer must never mistake our automated assessment for Wikipedia's own content.

**Colour.** White `paper` and `surface`, `surface-sunken` (#f8f9fa) for panels, `ink` #202122 for text and `ink-muted` #54595d for metadata. The only brand hue is `accent`, Wikipedia's link blue, used for links, the primary action, focus and selection. It never means a tier.

**The three tiers** are told apart by lightness, shape and a pip count, so hue is never the only cue:

| Tier | Badge | Chart mark | Pips |
|---|---|---|---|
| 1 · Removed by Wikipedia editors | infobox grey `tier-reverted-bg` | hollow circle `tier-reverted-mark` | ▮▯▯ |
| 2 · Disputed and removed | barely-warm tint `tier-contested-bg` | filled diamond `tier-contested-mark` | ▮▮▯ |
| 3 · Pattern consistent with a coordinated attempt | muted plum-grey tint `tier-campaign-bg`, dark text | filled square `tier-campaign-mark` | ▮▮▮ |

**Diffs** use a soft blue tint for added text (`diff-add-*`) and a soft orange tint with strike-through for removed text (`diff-del-*`), never green and red. The exact words that a signal or the assessment points at get a faint `evidence-bg` and a dashed underline. **Rule of thumb:** a screen should read as calm grey text with blue links. If a colour is the first thing you notice, tone it down.

**Type.** We use Wikipedia's own stacks, with no web fonts to load:
- **Serif** (Linux Libertine → Georgia) for page and section titles, and for *content under examination*: claim headlines (`t-claim`) and the prose diff. Titles are normal weight with a rule under them.
- **System sans** (-apple-system, Segoe UI, Roboto, Arial) for the interface.
- **Mono** (Menlo/Consolas) for wikitext and figures (`t-wikitext`, `t-data`).

**Space and shape.** 4-px base (`space-1` … `space-12`). Every corner is 2 px (`radius-sm`, `radius-md` and `radius-lg` are all 2 px): cards, buttons, chips, badges, inputs. Chips are not pills. Focus is a crisp 1-px blue border plus an inset line (`shadow-focus`), with no glow. `shadow-overlay` is reserved for tooltips and the reconnect dialog.

**Charts** are server-rendered inline SVG from Razor components, with no JS chart library. Named and anonymous edits are two lightnesses of slate (`chart-named`, `chart-anon`). Conflict episodes are shaded bands (`chart-band`). Incident markers reuse the tier marks. Every chart has a one-sentence text summary under it.

**Iconography.** We use few icons and no icon font. The inline SVGs are 1.6-px stroke, `currentColor` and 12–18 px, used only for signal chips, banners and empty states. There is no logo. The wordmark is type only: an italic serif **&** in `accent`, followed by “Ampersand of Truth” in the serif at normal weight, echoing Wikipedia’s own serif wordmark without copying it (see the app bar in the page previews). A real mark can replace it later.

## 4 · Using this in the Blazor app

- **`wwwroot/app.css`** = the contents of [`tokens.css`](tokens.css) (custom properties plus the Bootstrap mapping) followed by [`aot.css`](aot.css). Keep the `.aot-*` class names, because each one maps to one Razor component. Use `.aot-wikitable` for revision tables.
- **Bootstrap 5 stays.** Point its variables at our tokens instead of fighting it: `--bs-body-bg: var(--paper)`, `--bs-body-color: var(--ink)`, `--bs-link-color: var(--accent)`, `--bs-border-color: var(--line)`, `--bs-border-radius: 2px`, `--bs-font-sans-serif: var(--font-sans)`, `.btn-primary { --bs-btn-bg: var(--accent); … }`. Use Bootstrap for the grid, forms, `.visually-hidden` and collapse, and these classes for everything domain-specific.
- **Light theme first** (#42). The dark values exist and pass contrast, but don't spend time on a theme switch until P2.11 is done.
- **Test ids:** every interactive element in the previews names the `data-testid` the Razor version should carry (see *Build order*).

## 5 · Files in this folder

| File | What it is |
|---|---|
| `README.md` | This brand book. Read it first. |
| [`plan/1-flow-and-routes.md`](plan/1-flow-and-routes.md) | The flow, the routes, URL state, layout decisions and responsive rules |
| [`plan/2-screens.md`](plan/2-screens.md) | Annotated wireframes for every page, with the `data-testid`s each needs |
| [`plan/3-states-and-trust.md`](plan/3-states-and-trust.md) | The state matrix (loading, empty, errors, reconnect), trust patterns, the signal phrasebook and the copy rules |
| [`plan/4-build-order.md`](plan/4-build-order.md) | The Razor component inventory, what the UI needs from `Incident`, and the order of work |
| [`tokens.json`](tokens.json) | **Source of truth** for colours, type, spacing, radii and shadows, with a usage note on every token |
| [`tokens.css`](tokens.css) | `tokens.json` compiled to CSS custom properties, plus the Bootstrap variable mapping |
| [`aot.css`](aot.css) | The `.aot-*` component classes. Each one maps to one Razor component. |
| `components/<Name>/README.md` | Guidelines for each component |
| `components/<Name>/preview.html` | A static reference rendering of each component. **Open it in a browser**: it loads `tokens.css` and `aot.css` by relative path. `LandingPage`, `OverviewPage` and `IncidentPage` are full-page mock-ups at 1280 px. |

**This folder is the source of truth for the UI.** If the app and these files disagree, the files win, unless the team decides otherwise. In that case, update the files in the same PR. A visual copy also lives in a private claude.ai design system (Ampersand of Truth), but agents should read this folder.

The sections in `plan/` are the plan itself: **Flow & routes → Screens → States & trust → Build order**.
