### Eval scorecard · `s7-clusters-judged` · signals · test split

Golden `2026-11-14.1` (`m-2026-10`) · 4 topics, 4 requests · limit 50 · commit `d5779f50a2ef-dirty` · 2026-11-14 19:34:48 UTC · 0 failed

| Metric | Value |
|---|---:|
| strict_p@10 | 0.75 |
| strict_p@25 | 0.70 |
| lenient_p@10 | 1.00 |
| lenient_p@25 | 0.95 |
| unlabelled@10 | 6.50 |
| unlabelled@25 | 19.00 |
| strict_p@10_judged | 0.57 |
| strict_p@25_judged | 0.49 |
| unjudged@10 | 1 |
| unjudged@25 | 1 |
| recall | 0.65 |
| level_accuracy | 0.32 |
| overclaim_rate | 0.18 |
| duplicates | 0 |
| quiet_shown | 0 |
| quiet_contested_or_above | 0 |
| seconds_per_topic | 0.91 |
| llm_calls | 0 |
| input_tokens | 0 |
| output_tokens | 0 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | judged p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `language-politics` | topic | 50 | 1 | 5 | 2 | 0 | 0 | 42 | 1.00 | 0.90 | 6 / 7 | 0.9 |
| `sami-prehistory` | topic | 50 | 1 | 4 | 2 | 0 | 0 | 43 | 0.50 | 0.50 | 5 / 9 | 0.8 |
| `far-right` | topic | 50 | 1 | 2 | 3 | 1 | 0 | 43 | 0.50 | 0.30 | 3 / 7 | 0.8 |
| `national-myths` | topic | 50 | 1 | 2 | 4 | 5 | 0 | 38 | 1.00 | 0.60 | 3 / 3 | 1.1 |

**5 best** (highest-ranked true positives)

1. `language-politics` #1 → `language-politics/i02` contested, shown as reverted: Adds that Freudenthal called Finns an inferior 'Turanian' people, a medal section tying the SFP's Freudenthal…
2. `language-politics` #2 → `language-politics/i04` contested, shown as reverted: Puts back a sentence, cited to a 2002 Helsingin Sanomat editorial critical of the league, that the associatio…
3. `sami-prehistory` #2 → `sami-prehistory/i01` campaign, shown as reverted: Adds paragraphs, cited to one magazine article, saying the Sámi are not more indigenous than the Finns, that …
4. `far-right` #3 → `far-right/i04` contested, shown as reverted: Rewrites the IKL lead as a 'far-right fascist party that took many influences from German National Socialism …
5. `language-politics` #3 → `language-politics/i01` campaign, shown as reverted: Adds a section 'Fennomania in the internet era': the term now means opponents of compulsory Swedish, some of …

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `sami-prehistory/i02` campaign: Turns the redirect 'Metsäsaamelaiset' into an article stating that the Forest Sámi are one of the (four) Sámi… (page 1096144, `sami-prehistory`)
2. **missed** `far-right/i02` contested: Recasts de Wit's 'Laiton lehti?' publication as a 'vulgar nationalist blog' and adds that he became known for… (page 1395607, `far-right`)
3. **missed** `far-right/i03` contested: Adds a 'Criticism' section saying Turtiainen's pro-Russia line would likely drop Finnish living standards to … (page 1105958, `far-right`)
4. **missed** `far-right/i06` contested: Adds a paragraph saying the Lapua movement's first meetings were in Swedish-speaking parishes, that its Swedi… (page 11477, `far-right`)
5. **missed** `far-right/i07` contested: Changes the SKE infobox title of its secretary from 'party secretary' to 'secretary', on the grounds that SKE… (page 1445320, `far-right`)

**Unlabelled:** 166 shown incident(s) have no golden item → `research/38-s11-decision/results/s7-clusters-judged.test.unlabelled.jsonl` for the E3 judge.
Judged precision counts an unjudged hit as a miss: 1 unjudged in the top 10, 1 in the top 25 (all topics).

**Failures:** none.

**Cost:** 0.9 s per request (max 1.1 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run signals --split test --name s7-clusters -- python3 research/34-s7-repeat/variant.py
```

</details>
