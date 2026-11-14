# StageTracker
The live run: four stages with counts, driven by pipeline events. It collapses to a one-line summary when done.

- Stages: *Finding articles → Scanning edit history → Reading suspicious edits → Assembling incidents*.
- Done = an `ok` tick. Active = an `accent-tint` row and a spinner ring (static under `prefers-reduced-motion`). Pending = muted.
- *Cancel* is always present while running. A failed stage swaps in a `StatusBanner` error with *Retry stage*.
- Warm runs render the collapsed summary straight away: “loaded from previous analysis, 2 h ago · Re-run”.
- `role="status"` with `aria-live="polite"`. Announce stage changes, not every count tick.
