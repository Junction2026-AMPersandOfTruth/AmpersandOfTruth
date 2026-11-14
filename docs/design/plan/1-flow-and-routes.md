# Flow & routes

## The flow

```
  ┌──────────┐  chip / Enter   ┌───────────────────────────────┐  card   ┌─────────────────┐
  │ Landing  │ ──────────────▶ │ Investigation                 │ ──────▶ │ Incident detail │
  │   /      │                 │  progress ─▶ overview (1 page)│ ◀────── │  prev ◀ ▶ next  │
  └──────────┘                 └───────────────────────────────┘  back   └─────────────────┘
       ▲  out of scope →             │ article in rail                  │ article / account
       │  nearest in-scope           ▼                                  ▼
       │  suggestions         ┌──────────────────┐             ┌──────────────────┐
       └───────────────────── │ Article timeline │             │ Account (stretch)│
             new search       └──────────────────┘             └──────────────────┘
                     /methodology is linked from every tier badge (“why this tier?”) and the footer
```

**Click budget for the flagship demo:** landing → suggested topic chip (1) → top incident card (2) → understood. The third click is spare for the diff toggle or the Wikipedia link.

**Progress and overview are one page, not two.** The stage tracker sits on top and collapses into a one-line summary when the run completes, and cards fill the list underneath as they arrive. You can open an incident before the run finishes (#45).

## Routes

| Route | Page | Feature folder | Issue |
|---|---|---|---|
| `/` | Landing: search, suggested topics, scope line, how it works, recent investigations | `Features/Search` | P2.3 #44 |
| `/investigate/{topic}` | Live progress → results overview | `Features/Investigations` | P2.4 #45, P2.5 #46 |
| `/investigate/{topic}/incidents/{incidentId}` | Incident detail, full page, prev/next | `Features/Investigations` | P2.7 #48 |
| `/articles/{pageId}` | Article timeline + revisions in range | `Features/Articles` | P2.6 #47 |
| `/accounts/{name}` *(stretch)* | Account activity within the corpus | `Features/Accounts` | P2.8 #49 |
| `/methodology` | Pipeline diagram, tiers, measured quality, limitations, attribution | `Features/Methodology` | P2.10 #51 |

`{topic}` is a URL slug of the normalised topic text (`winter-war`, `talvisota`). The investigation is keyed in SQLite by normalised topic + pipeline version, so a refresh, a shared link or a circuit reconnect re-attaches to the same cached run instead of starting a new one.

## State lives in the URL

Blazor Server loses in-memory state on reconnect, so anything a user would expect to survive a refresh is in the route or the query string:

| State | Where |
|---|---|
| Topic, selected incident | route |
| Overview sort / filters | `?sort=exposure&tier=2,3&article=12345&from=2014&to=2016&signal=citation-removed` |
| Article time range | `/articles/12345?from=2014-01&to=2015-06&incident=inc_7f3a` (`incident` highlights its marker) |
| Diff view mode | `?view=split&text=wikitext&lang=fi` on the incident page (defaults: inline, prose, en) |
| Language preference | browser `localStorage` (`aot.lang`): it is a per-viewer convenience, not shared state |

Filters update the URL with `NavigationManager.NavigateTo(uri, replace: true)` so they don't flood history. Back from the incident page returns to the overview with filters intact.

## Layout decisions (from the options in #42)

| Decision | Choice | Why |
|---|---|---|
| Overview layout | **A, incident-first:** a ranked card list with an article rail on the right and a compact cross-article timeline strip on top. B (article-first) is reachable per article through the rail. C (timeline-first) survives only as the strip. | Answers “what was attempted?” in one glance, and the strip still gives the striking time picture. |
| Incident detail | **Full page** with prev/next and ← → keys, not a drawer | Shareable URL, room for a side-by-side diff, and judges can be sent a link. |
| Article view | Full page | The chart needs the width. |
| Theme | **System default + Light / Dark / System toggle** in the app bar (`localStorage['aot.theme']`) | Follows `prefers-color-scheme` by default (including without JS) and remembers an explicit choice per browser without flashing on load. |
| Charts | Inline SVG rendered by Razor, re-rendered for zoom | No JS interop, bUnit-testable, works with Interactive Server. |

## Responsive

The two widths that matter are **1280×720** (projector) and **a phone held by a judge (390 px)**.

- ≥ 992 px: two columns (`1fr 320px`) on the overview and incident pages.
- < 992 px: one column. On the overview the article rail becomes an *Articles (14)* disclosure above the list. On the incident page the side panels (Why flagged, Assessment, Related) move below *What changed*, in that order.
- < 768 px: the diff split toggle is hidden (inline only), the timeline strip keeps its full axis at a smaller height, and the stage tracker becomes a 2×2 grid.
- No horizontal page scroll anywhere. Long Finnish compounds and URLs use `overflow-wrap: anywhere`.
