# S11 red team (#38): the app's output for the three demo topics

Run 2026-11-14 on the **real pipeline** at version 4 (`main` after #126), with the demo topics as a user types them
(`research/41-regression/events/`). Re-insertion chains are merged here with the rule the cloud lane is implementing.
The red-team agents saw only incident evidence (judge-style inputs: pseudonymised editors, masked summaries, the
`wikidiff`, history signals) and never the experiment discussions.

## 1. Sceptic (top 10 per demo topic; 24 unique incidents; `adversarial-review@1` on `gemini-4-carbon` and `gemini-4-argon`)
- **21 survive on both models.**
- **1 benign on both:** civil war, *"The Civil Guard was clearly Finland's largest civic organization"*. The added text
  **is still in the article**: the evidence standard's `kept` exclusion, which the app does not wire yet
  (`KeptInLatestRevision`, listed as "not yet" in #120).
- **1 split** (carbon benign, argon survives): Continuation War, the Ošta/Koromyslovo passage. Its removal was a
  **block-evasion revert** (procedural), not a content objection, so it should not count as a community dispute.
- Quotes, revision ids, timestamps, diff links and IP masking were already verified for every shown incident
  (#41, `research/41-regression/results/check.v4.md`).
- `sceptic.out.jsonl` holds every verdict with its reason and `would_change_mind`.

## 2. Miss-hunter (data-driven: golden labels + research hits the judge rated contested and the reviewer let survive)
| Topic | golden contested/campaign | on the app's articles | shown | missed there | on articles not retrieved | research contested+ hits not shown |
|---|---:|---:|---:|---:|---:|---:|
| civil-war-1918 | 10 | 6 | 4 | 2 | 4 | 5 |
| continuation-war | 7 | 4 | 2 | 2 | 3 | 7 |
| winter-war | 5 | 4 | 3 | 1 | 1 | 7 |

Notable misses:
- `civil-war-1918/i02` ("not a civil war but the War of Liberation", lead rewrite) and `i08` (White terror toll
  20–25k).
- `continuation-war/i04`: a partial removal, the known identity-revert blind spot.
- `winter-war/i05` ("hatred of Russians was a key to success").

Details in `miss-hunter.md`. The levers are a larger candidate pool than the top 60, and retrieval: about a third of the
golden items sit on articles R3-D did not return.

## 3. Fairness
- **Editor kind:** no skew. Anonymous-only inserters make up 48 % of level-1 and 48 % of level-2+ incidents; new named
  accounts (< 30 days) 22 % vs 18 % (`fairness.editors.json`).
- **Side** (one `gemini-4-carbon` pass per topic over its level-2+ claims, `side.<topic>.md`):
  - Continuation War: balanced, 5 vs 7.
  - Winter War: balanced, 5 vs 7.
  - **Civil war: skewed**, 11 White-framing vs 2 Red-framing.
  - The human-labelled golden items for the civil war split 5 vs 3 (`side.civil-war-1918.golden.md`). Real pushes lean
    White, but the tool's 5.5:1 is about 3× steeper.
  - The likely causes are: duplicate retries of the same White-framing edits (the dedupe fixes those), higher
    sensitivity to naming edits than to victim-count framing, and two missed Red-side items (`i08` above). n is small.

## Hand-off (cloud lane, #39)
1. Wire `KeptInLatestRevision`: the added text is still present in the latest revision → `kept` exclusion.
2. Do not count a block-evasion / sockpuppet revert (S5 `block_evasion`) as a dispute signal (`removal_reason`).
3. The dedupe (in progress) and the username scrub in classifier prompts (filed on #39).
4. After 1–3: re-run this red team and the civil-war side check (`research/38-s11-decision/redteam/`).
