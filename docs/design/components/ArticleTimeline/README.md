# ArticleTimeline
One article's history as a picture: monthly edits stacked named/anonymous, conflict episodes shaded, incidents marked, and article size in a second lane.

- Bars: `chart-named` (bottom), `chart-anon` (top). Bands: `chart-band`. Marks: tier shapes on a dashed drop line. Size lane: an `accent` line, where sudden drops mean blanking.
- Brush on the size lane selects a range (`accent-tint`) → re-render zoomed and list that period's revisions below.
- Always followed by a text summary: “Most contested period: Nov 2014 – Mar 2015 (37 removals).”
- `Spark=true` draws bars only, 20 px tall, with no axes (used in `ArticleRailItem`).
- Data comes from precomputed monthly aggregates (P1.S1/S4), never raw revisions.
