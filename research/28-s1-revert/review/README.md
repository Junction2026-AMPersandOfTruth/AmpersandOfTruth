# Independent review of the S1 revert classes (#28)

#28 asks a second agent to judge 20 sampled reverts per class for misclassification. A Gemini worker that had not built
the classifier read the actual `wikidiff` output for all 140 sampled reverts (`results/sample_<class>.md`), together
with the masked summaries and page history, and recorded one verdict per revert. The orchestrator (Claude) fixed two bugs in
`review.py`: a missing column and a sample-order check. Claude also wrote this README from `verdicts.jsonl`.

```bash
python3 research/28-s1-revert/review/review.py                  # ≤ 300 cached text fetches, diffs, writes verdicts.jsonl + tables
python3 research/28-s1-revert/review/review.py --inspect dispute # masked diffs + context for one class
```

## Agreement: 81 / 140 (57.9%)

| S1 class | Expected verdict | Agrees | Rate |
|---|---|---:|---:|
| `self_revert` | `self_correction` | 20 / 20 | **100%** |
| `graffiti_cleanup` | `graffiti` | 15 / 20 | 75% |
| `edit_war` | `edit_war` | 12 / 20 | 60% |
| `bot` | `bot_maintenance` | 11 / 20 | 55% |
| `other` | `good_faith_fix` | 11 / 20 | 55% |
| `excluded` | `collision_artifact` | 6 / 20 | **30%** |
| `dispute` | `content_dispute` | 6 / 20 | **30%** |

| S1 class \ verdict | graffiti | good_faith_fix | content_dispute | edit_war | self_correction | bot_maintenance | collision_artifact |
|---|---:|---:|---:|---:|---:|---:|---:|
| `excluded` | 1 | 1 | 3 | 8 | 0 | 1 | **6** |
| `bot` | 0 | 0 | 0 | 0 | 0 | **11** | 9 |
| `self_revert` | 0 | 0 | 0 | 0 | **20** | 0 | 0 |
| `edit_war` | 4 | 4 | 0 | **12** | 0 | 0 | 0 |
| `dispute` | 4 | 10 | **6** | 0 | 0 | 0 | 0 |
| `graffiti_cleanup` | **15** | 4 | 0 | 1 | 0 | 0 | 0 |
| `other` | 5 | **11** | 4 | 0 | 0 | 0 | 0 |

## Systematic errors and proposed rule changes

The flip counts are out of these 140 rows and are approximate: they come from the reviewer's notes, not from re-running the rules.

1. **`dispute` is too loose. This matters most, because `dispute` is the class the incident search feeds on.** "Any
   reverted user is a named account" catches registered accounts with 1–3 edits that write graffiti (racial slur, toilet humour,
   leetspeak: 4 rows), plus routine cooperative fixes such as commas, a category move, an image swap, an expired protection template, and an
   unsourced number that FlaggedRevs rejected (10 rows).
   **Fix:** (a) treat a named account with < 5 corpus edits like an anonymous one (S6 `s6_editor_profile.edits`, or the
   `throwaway` flag); (b) require a substantive prose change (`wikidiff` `is_substantive` and `added_prose_chars` or
   `removed_prose_chars` ≥ 30); otherwise the class is `other` / `good_faith_fix`. **Flips about 12 of 14.**
2. **`excluded` throws away real conflicts.** `restored_bytes ≤ 100` catches **redirect wars**, where an article is turned back into
   a redirect and reverted again (6 rows, all real `edit_war`/dispute). `wide_span > 15` catches legitimate mass rollbacks of
   one account's long edit run, among them an admin rollback of 72 revisions with page protection, and an RfC-backed revert (6 rows).
   **Fix:** make `blanking_collision` only `r_bytes = 0`, or `restored ≤ 100 B` **and** a gap of more than 365 days between the
   restored revision and R. Make `wide_span` require that the span covers more than 30 days **or** at least 3 distinct non-bot users;
   a long run by one account within days is a normal rollback. **Flips about 12 of 14.**
3. **`bot` includes the 2013 Wikidata interwiki migration:** a bot removes an interwiki link after copying it to
   Wikidata, which recreates an older sha1 (9 rows). **Fix:** a bot reverter whose diff removes only `interwiki` segments
   becomes `excluded` (`interwiki_migration`). **Flips 9.**
4. **The `edit_war` rule "the span contains a revert" fires on noise:**
   - the same patroller undoing two edits one after the other;
   - an unrelated revert months earlier inside the span;
   - an anonymous vandal re-inserting the same graffiti, which is repeat vandalism, not a war of editors (7 rows).

   **Fix:** count a revert inside the span only when it is by a reverted user of R (a real counter-revert), and it falls
   within 7 days of R. Require at least one named, non-throwaway account on the reverted side; otherwise the class is
   `graffiti_cleanup`. **Flips about 7 of 8.**
5. **`graffiti_cleanup` and `other`** are acceptable for their purpose. `other` (anonymous edits surviving more than 24 h) is a
   mix of good-faith fixes (11), graffiti (5) and disputes (4); it is the class where S5's removal reasons or S9 add the most.

## What this means for downstream work

- The S1 baseline signals variant ranks `dispute` and `edit_war` first. With these rules, about half of what it
  ranks first is not a dispute. Expect a weak **strict** precision for the baseline: that is the naive baseline doing its job,
  and it is the gap S5/S6/S9 have to close.
- Fixes 1 and 4 need S6 (`s6_editor_profile`) and `wikidiff`, so they belong in a rebuilt class column (`class_v2`). Do not
  silently change `class`, because S4/S5/S6 were built on it.
