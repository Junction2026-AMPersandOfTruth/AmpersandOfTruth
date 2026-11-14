# SignalChip
A detected signal, phrased in plain language, with the full sentence in its tooltip. The chip is a summary: the evidence lives in `WhyFlagged`.

- Text comes from the signal phrasebook (*States & trust*), keyed by the pipeline's signal id. Never show raw ids like `reinserted_after_revert`.
- Each chip has a 12 px stroke icon: redo (re-insertion), struck lines (citation removed), person (accounts), stacked pages (cross-article), hash (number changed), speech (removal reason).
- As a filter it renders as a `button` with `aria-pressed`.
