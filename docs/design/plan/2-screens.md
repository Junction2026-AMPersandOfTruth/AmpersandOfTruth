# Screens

Each screen has a live mock in **Components → Pages** (LandingPage, OverviewPage, IncidentPage). The wireframes below are the annotated versions.

## Landing `/` (P2.3)

```
┌ & Ampersand of Truth ─────────────────────────── Investigate  Methodology ┐
│                                                                           │
│   What was pushed into Wikipedia?                         (t-display)     │
│   ┌──────────────────────────────────────────────┐ [Investigate]          │
│   │ What topic do you want to investigate?        │  Enter submits         │
│   └──────────────────────────────────────────────┘                         │
│   Covers 19,742 Finnish Wikipedia articles on the history of Finland,     │
│   edits 2003–2026. Ask in any language.                                   │
│   Try: (Winter War) (Civil War 1918) (Mannerheim) (Continuation War) …     │
│   ▸ Filters (date range, minimum tier)  ← collapsed                         │
│                                                                           │
│   ① Find articles      ② Read 20 years of edits    ③ Show what was        │
│     vector search        reverts, re-insertions,     pushed and removed,   │
│     over the corpus      removed sources             with the evidence     │
│                                                                           │
│   Recent in this browser: Winter War · Lapland War                         │
└ Wikipedia text CC BY-SA 4.0 · Data: FineWiki, Wikimedia dumps · Method ───┘
```

- The **suggested-topic chips matter most** on this screen. The corpus only covers the history of Finland, and a judge who types “climate change” must not hit a dead end. Use the demo topics plus 3–4 known-good ones.
- **Out of scope:** stay on landing, show an inline `EmptyState` (“Our data covers the history of Finland. ‘Climate change’ is outside it.”) with the nearest in-scope topics from P1.R6 as chips.
- Validation: trimmed, 2–120 chars, `EditForm FormName="topic-search"`.
- Test ids: `topic-input`, `topic-submit`, `topic-chip`, `filters-toggle`, `recent-item`, `out-of-scope`.

## Investigation `/investigate/{topic}`: progress → overview (P2.4 + P2.5)

```
┌ app bar (with compact search) ───────────────────────────────────────────┐
│ Winter War                                             [Copy link]       │
│ 14 articles · 9,214 revisions scanned · 6 incidents: ▮▮▮ 1  ▮▮▯ 3  ▮▯▯ 2   │
│ ┌ ✓ Finding articles 14 │ ✓ Scanning history 9,214 │ ◌ Reading edits 4/11 │ … │ [Cancel] ┐
│ └ collapses to one line “Analysed in 38 s · loaded from previous analysis” │
│ ┌ timeline strip 2003 ─────────●──◆──■───◆─────○──────── 2026 ┐ legend     │
├──────────────────────────────────────────────┬─────────────────────────────┤
│ Sort [Relevance ▾]  Tier (1)(2)(3)  Signal ▾   │ ARTICLES (14)               │
│ ┌ IncidentCard ─────────────────────────────┐  │ Talvisota · Winter War   3  │
│ │ ▮▮▮ Pattern consistent…       Talvisota    │  │ ▁▂▁▅█▂▁ sparkline          │
│ │ “Finland started the Winter War by …”      │  │ matched: title + 2 sections │
│ │ FI: Suomi aloitti sodan …                  │  │ Mainilan laukaukset      2  │
│ │ Inserted 4 times by 3 accounts over 2 yrs; │  │ …                           │
│ │ live for 61 days in total.                 │  │                             │
│ │ First seen · Last removed · Live · Accts   │  │                             │
│ │ (re-inserted after revert) (removed cite)  │  │                             │
│ └───────────────────────────────────────────┘  │                             │
│ [skeleton card] ← pending while scoring        │                             │
│ ▸ Reverted edits we did not flag (23)          │                             │
└──────────────────────────────────────────────┴─────────────────────────────┘
```

- **Summary header** counts tiers with the pips, so the colour legend teaches itself.
- **Stage tracker**: *Finding articles → Scanning edit history → Reading suspicious edits → Assembling incidents*, each with a live count. Warm runs skip the animation and show “Loaded from previous analysis · 2 h ago” with a *Re-run* link.
- **Timeline strip**: one row per article for the top 5 articles, plus an “other” row. Each incident is a tier mark. Hovering a card highlights its mark and vice versa (`is-highlighted` / `is-hot`), and clicking a mark scrolls to its card.
- **Sort**: Relevance (default) / Most recent / Longest live. **Filter**: tier chips, article (from the rail), time range (brush on the strip), signal type.
- **Article rail**: the match reason (“title match”, “section ‘Mainila shots’”), a 20-year sparkline, and the incident count. Clicking the title opens `/articles/{id}`, and clicking the count filters the list.
- **“Reverted edits we did not flag (23)”**, collapsed. Shows what was considered and dismissed (tier 0: vandalism, formatting). This builds trust and helps us debug.
- **Empty:** “No disputed insertions found for this topic”, plus what was checked (14 articles, 9,214 revisions, 41 reverts, all judged vandalism/formatting), plus a link to the reverted-edits list.
- Test ids: `summary-header`, `stage-{n}`, `cancel-run`, `timeline-mark`, `incident-card`, `sort-select`, `tier-filter`, `article-rail-item`, `unflagged-toggle`.

## Incident detail `/investigate/{topic}/incidents/{id}` (P2.7)

```
┌ Winter War › Incidents › 1 of 6                        [◀ Prev] [Next ▶]  ┐
│ ▮▮▮ Pattern consistent with a coordinated attempt  · why this tier?         │
│ “Finland started the Winter War by shelling Mainila.”     (t-claim-lg)      │
│ FI  Suomi aloitti talvisodan ampumalla Mainilan kylään.   [Machine transl.] │
│ First inserted 3 Mar 2014 · Live 61 days total · 3 accounts · 2 articles    │
├──────────────────────────────────────────────┬─────────────────────────────┤
│ What changed                                  │ Why this is flagged         │
│ [Inline|Split] [Prose|Wikitext] [EN|FI]       │ Disputed and removed —      │
│ …the war began on 30 November 1939. ⋯6 lines │ because:                    │
│ Soviet troops crossed the border after        │ ▸ Re-inserted after revert ×3│
│ +Finland shelled Mainila on 26 November+       │ ▸ Removed a citation         │
│ -the Mainila shots, staged by the NKVD[1]-     │ ▸ 3 new accounts, 9 days     │
│ ── EN (machine translation) ──                │ ▸ Same claim in 2 articles   │
│                                               │ ───────────────────────────  │
│ Sequence of events                            │ AUTOMATED ASSESSMENT         │
│ [+] 3 Mar 2014 14:02  Pohjantähti77  live 4 d  │ Category: historical denial  │
│     “korjattu historiallinen fakta” (EN: …) ↗ │ “…shelled Mainila…” quoted   │
│ [−] 7 Mar 2014  Kumoaja  “lähteetön” ↗        │ Confidence ▮▮▯ medium        │
│ [+] 2 Feb 2015  85.76.x.x   live 41 d   ↗     │ Uncertainty: …               │
│ …                                             │ ───────────────────────────  │
│                                               │ Related · Actions            │
└──────────────────────────────────────────────┴─────────────────────────────┘
```

Section order follows #48: header → **What changed** → **Sequence of events** in the main column. **Why flagged**, **Automated assessment**, **Related** and **Actions** go in the side column, and on phones they stack after the diff.

- **Header**: the claim in English as the headline, with the Finnish original directly under it and labelled `FI`. The tier badge has “why this tier?”, which jumps to the Why panel. A key-facts row follows.
- **Diff**: inline by default with split as an option, prose (cleaned wikitext) by default with raw wikitext as an option, and an EN/FI toggle. Unchanged context folds like a code diff (“⋯ 6 unchanged lines”). Evidence spans from signals get highlighted. All inserted text is rendered as **encoded text, never `MarkupString`**.
- **Deletion incidents** (a source or fact was removed) use the same layout. The headline then becomes “Removed: <what was removed>”, and the diff leads with the `del` span.
- **Sequence of events**: one row per member edit, showing +/− glyph, timestamp UTC, `EditorLabel`, edit summary (EN machine translation with the FI original), time live, and a “View on Wikipedia ↗” link to the exact diff. Gaps between rows read “… 11 months later”.
- **Assessment**: always labelled *Automated assessment*, with its category, reasoning, quoted evidence (highlighted in the diff too), a confidence meter, and an uncertainty note.
- **Actions**: Copy link · Save (P4.1) · Trace on the web (P3, hidden until built) · This looks wrong (P4.4).
- **Keyboard**: ← → for prev/next, `Esc` back to the overview.
- Test ids: `incident-header`, `diff-view-toggle`, `diff-text-toggle`, `diff-lang-toggle`, `diff-fold`, `event-row`, `wiki-diff-link`, `why-signal`, `assessment`, `prev-incident`, `next-incident`.

## Article `/articles/{pageId}` (P2.6)

```
│ Winter War  ·  FI Talvisota ↗  ·  3,104 revisions 2003–2026  · 3 incidents │
│ [ArticleTimeline: monthly bars named/anon, conflict bands, incident marks]  │
│ [size lane: bytes over time]                                               │
│ Most contested period: Nov 2014 – Mar 2015 (37 reverts).  ← text summary   │
│ ── Revisions 1 Nov 2014 – 31 Mar 2015 (drag on chart to change) ──         │
│ time · editor · summary (EN/FI) · ±bytes · [reverted] · diff ↗              │
```

- The chart is the `ArticleTimeline` component. Dragging selects a range, which re-renders the chart zoomed and lists that period’s revisions.
- The same component at 20 px height is the `aot-spark` sparkline in the article rail.

## Methodology `/methodology` (P2.10)

A plain document page:

1. **The pipeline as five boxes:** topic → articles → edit history → signals → assessment, drawn as inline SVG.
2. **What each tier means**, with its minimum evidence (the P0.7 table).
3. **Measured quality** from P1.S11, precision included, stated honestly.
4. **Coverage:** which articles and which years, Finnish only.
5. **Limitations:**
   - Reverted ≠ false, and unreverted ≠ true.
   - Intent can’t be established from edits.
   - Activity outside the dataset is invisible.
   - Machine translation and model assessments can be wrong.
6. **Attribution and licences.**

## Account `/accounts/{name}` (P2.8, stretch)

Build this only after the flow above is solid. Use the `EditorLabel` rules throughout. The page shows the account's activity sparkline, the articles it edited, the other incidents it appears in, and a link to its contributions on fi.wikipedia.org, with a visible caveat: *“Only edits within our dataset of 19,742 articles are shown.”*
