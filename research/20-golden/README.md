# P1.E1 golden set (#20): how `research/eval/golden.json` was built

**Result (version `2026-11-14.1`, dataset `m-2026-10`):** 13 topics + 1 quiet control + 2 out-of-corpus controls,
62 queries, 256 graded articles, 329 items: 195 incidents (106 reverted, 84 contested, 5 campaign) and 134 non-incidents.
`validate.py golden --final` passes. Split: dev = 9 topics (63 contested, 1 campaign); **test = 4 topics, sealed**
(language-politics, sami-prehistory, far-right, national-myths: 21 contested, 4 campaign) + `moon-landing-hoax`.

## Pipeline
1. `topics.json`: the 10 P0.6 shortlist topics + 3 from corpus stats, seed pages, P0.6-cited revisions.
2. `extract.py` → `packs/<topic>.json` (git-ignored). One serial corpus job per topic: identity-revert runs (null
   revisions dropped, re-insertion chains linked by sha1), readable diffs, look-alike non-incidents, partial removals
   with a dispute summary (insertion found by text search and verified), and article candidates from vector + FULLTEXT
   search. Editors are per-topic pseudonyms; IPs masked; names after "by user X" phrases masked.
3. **Labeller** (one Claude Opus agent per topic, `LABELLER.md`) → `labels/<topic>.labeller.json`. Reads only its pack.
4. **Blind re-labels**, item ids shuffled so `i`/`n` prefixes do not leak (`blind_input.py`, `BLIND.md`):
   Claude Sonnet on 9 topics (`*.blind.json`), Gemini `gemini-4-carbon` on all 14 in one structured call per topic
   (`gemini_blind_api.py`, `*.blind-gemini.json`). Before inputs reach Gemini, `scrub_inputs.py` removes every
   corpus account name from summaries.
5. `merge.py` (+ `human.json`) → `research/eval/golden.json` and `REVIEW.md`.
   Gates: `verify_revs.py` (every rev id exists on the item's page, none is a null revision) and `privacy_check.py`
   (no account names or IPs in any text field; remaining hits are article content such as place and historical names).

## Agreement with the labeller
| Pass | Items | Same label |
|---|---:|---:|
| Claude Sonnet blind | 201 | 172 (86 %) |
| Gemini blind | 329 | 271 (82 %) |

## Human sign-off (key44 with a teammate, 2026-11-14)
| How the label was settled | Items |
|---|---:|
| Decided one by one by the humans (disagreements in the demo topics, nato-debate, finlandization) | 17 |
| All passes agreed on contested/campaign → accepted on a **spot check** (not every diff read) | 70 |
| Passes disagreed, no individual decision → **labeller's label** (the review found the labeller the most reliable pass) | 62 |
| All passes agreed below contested (reverted / not-incident) | 180 |

Each item's `labelled_by` and `notes` say which of these applies and keep every pass's label, so any item can be
re-opened. The policies live in `human.json` → `_policy`.

## Known limits
- Ground truth is shaped by identity reverts: partial removals are included but few, and rewrites that were never
  reverted are invisible (P0.6 caveat 4).
- **Only one `campaign` item in dev**: S10 cannot tune level-3 thresholds on dev; treat level-3 results as indicative.
- Re-insertions that sit outside a pack could not be listed (checker); some items name them in `notes` only.
- `LABELLER-NOTES.md` lists every labeller's least-sure items, for anyone who wants to challenge a label.
