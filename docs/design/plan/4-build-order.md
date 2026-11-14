# Build order

## Component inventory → Razor

Shared means it is used by two or more features and lives in `Components/Shared/`. Everything else lives in its feature folder. Each component has a preview in this system under the same name.

| Razor component | Folder | Key parameters | Used by |
|---|---|---|---|
| `TierBadge` | Shared | `Tier Tier`, `bool ShowWhy` | card, incident, strip legend, methodology |
| `SignalChip` | Shared | `Signal Signal` (id → phrasebook text + tooltip) | card, why panel, filters |
| `EditorLabel` | Shared | `string User`, `bool Anonymous` | event rows, account view, revisions table |
| `MtLabel` | Shared | `string Original`, `string Lang` | claim, diff, summaries, titles |
| `IncidentCard` | Shared | `Incident Incident`, `bool Highlighted`, `EventCallback OnHover` | overview, related, saved (P4.1) |
| `ArticleTimeline` | Shared | `ArticleAggregates Data`, `DateRange? Range`, `bool Spark`, `IEnumerable<IncidentMark> Marks` | article page, rail sparkline, dashboard (P4.6) |
| `EmptyState` | Shared (exists) | `Title`, `ChildContent`, `Actions` | all pages |
| `StatusBanner` | Shared | `Kind {Info, Warn, Error}`, `Title`, `Detail`, `OnRetry` | all pages |
| `TopicSearch` | Features/Search | `bool Compact`; `FormName="topic-search"` | landing, app bar |
| `StageTracker` | Features/Investigations | `RunProgress Progress`, `OnCancel` | overview |
| `TimelineStrip` | Features/Investigations | `IReadOnlyList<Incident>`, `string? HotId`, `OnMarkClick` | overview |
| `ArticleRailItem` | Features/Investigations | `RetrievedArticle Article` | overview |
| `DiffViewer` | Features/Investigations | `DiffPassage Passage`, `DiffMode`, `TextMode`, `Lang` | incident |
| `EditSequence` | Features/Investigations | `IReadOnlyList<IncidentEdit>` | incident |
| `WhyFlagged` | Features/Investigations | `Incident Incident` | incident |
| `AssessmentNote` | Features/Investigations | `Assessment? Assessment` (null → unavailable state) | incident |

## What the UI needs from the `Incident` contract (input to P1.S10 #37)

The UI is built against fixtures with exactly this shape, so it can start before the pipeline exists:

```csharp
record Incident(
  string Id, Tier Tier, string TierReason,            // TierReason: the "because …" sentence
  string ClaimEn, string ClaimOriginal, string Lang,   // claim headline + FI original
  IncidentKind Kind,                                   // Insertion | Deletion | CitationSwap | NumberChange
  IReadOnlyList<ArticleRef> Articles,                  // page_id, fi title, en title?
  DateTimeOffset FirstSeen, DateTimeOffset LastRemoved,
  TimeSpan TotalLive, int AccountCount, int InsertCount,
  IReadOnlyList<Signal> Signals,                       // id + evidence text + optional span offsets
  IReadOnlyList<IncidentEdit> Edits,                   // rev_id, parent_id, ts, user, anonymous, comment, action(+/-), live
  DiffPassage Passage,                                 // before/after prose + wikitext, changed spans, evidence spans
  Assessment? Assessment,                              // category, reasoning, quotes[], confidence(0-1), uncertainty
  double Relevance, string? ClusterId);
```

Pipeline events for the tracker:
`ArticlesFound(n, items)`, `RevisionsScanned(n)`, `CandidateScored(i, of, Incident?)`, `StageFailed(stage, error)` and `Completed(fromCache)`.

## Order of work (≈ 12 h budget)

| # | Step | Issue | Est. | Can start |
|---|---|---|---|---|
| 1 | **Tokens + `app.css` + Bootstrap variable mapping (2-px radius, link blue, system fonts)**; app bar, footer, page titles; delete `Features/Notes` | P2.2 #43 | 1 h | now |
| 2 | **Fixtures:** 3 demo topics × ~6 incidents as JSON matching the contract above, plus a `FakePipeline` that replays them with delays | P2.1 #42 | 1 h | now |
| 3 | Shared atoms: `TierBadge`, `SignalChip`, `EditorLabel`, `MtLabel`, `StatusBanner` + bUnit | n/a | 1 h | after 1 |
| 4 | Landing + `TopicSearch` + chips + out-of-scope | P2.3 #44 | 1 h | after 3 |
| 5 | `IncidentCard` + overview list, sort, filters, rail, URL state | P2.5 #46 | 2 h | after 2, 3 |
| 6 | `StageTracker` + streaming from `FakePipeline`, then the real P1.P1 | P2.4 #45 | 1.5 h | after 5 |
| 7 | Incident page: header, `DiffViewer`, `EditSequence`, prev/next | P2.7 #48 | 2 h | after 5 |
| 8 | `WhyFlagged`, `AssessmentNote`, methodology page, footer attribution | P2.10 #51 | 1 h | after 7 |
| 9 | `ArticleTimeline` (SVG) + article page + rail sparkline + overview `TimelineStrip` | P2.6 #47 | 1.5 h | after 2 |
| 10 | Translation toggles wired to `IEvidenceTranslator` | P2.9 #50 | (with P2.9) | after 7 |
| 11 | States pass + reconnect dialog + phone layout | P2.11 #52 | 1 h | after 4–9 |
| 12 | Review rounds ×2 with screenshots | P2.12 #53 | n/a | after 7, then after 11 |

**Parallel lanes:** steps 1–3 come first, done by one person. After that, lane A takes 4 → 6, lane B takes 5 → 7 → 8, and lane C takes 9. Lanes collide only on `NavMenu.razor` and `Program.cs`, one line each, as CLAUDE.md intends.

## Definition of done for each screen

- It works on fixtures, with the real pipeline behind a flag.
- Every state in the matrix has a bUnit test, and the route is added to `AppSmokeTests`.
- `data-testid` attributes match the ids listed under Screens.
- It reads correctly at 1280×720 and 390 px, with no horizontal scroll.
- It contains no untranslated Finnish without an MT tag, and no tier string outside `TierBadge`.
