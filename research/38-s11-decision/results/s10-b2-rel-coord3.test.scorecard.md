### Eval scorecard · `s10-b2-rel-coord3` · signals · test split

Golden `2026-11-14.1` (`m-2026-10`) · 4 topics, 4 requests · limit 50 · commit `d5779f50a2ef-dirty` · 2026-11-14 19:36:50 UTC · 0 failed

| Metric | Value |
|---|---:|
| strict_p@10 | 0.83 |
| strict_p@25 | 0.53 |
| lenient_p@10 | 1.00 |
| lenient_p@25 | 0.96 |
| unlabelled@10 | 6.75 |
| unlabelled@25 | 18.75 |
| recall | 0.50 |
| level_accuracy | 0.56 |
| overclaim_rate | 0.33 |
| duplicates | 0 |
| quiet_shown | 0 |
| quiet_contested_or_above | 0 |
| seconds_per_topic | 13.34 |
| llm_calls | 239 |
| input_tokens | 621,301 |
| output_tokens | 92,541 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `language-politics` | topic | 26 | 1 | 3 | 2 | 0 | 0 | 20 | 1.00 | 4 / 7 | 13.6 |
| `sami-prehistory` | topic | 40 | 1 | 2 | 2 | 0 | 0 | 35 | 1.00 | 3 / 9 | 10.8 |
| `far-right` | topic | 50 | 1 | 2 | 5 | 0 | 0 | 42 | 0.33 | 3 / 7 | 14.7 |
| `national-myths` | topic | 38 | 1 | 2 | 3 | 2 | 0 | 30 | 1.00 | 3 / 3 | 14.3 |

**5 best** (highest-ranked true positives)

1. `far-right` #1 → `far-right/i04` contested, shown as campaign: Rewrites the IKL lead as a 'far-right fascist party that took many influences from German National Socialism …
2. `language-politics` #1 → `language-politics/i02` contested, shown as campaign: Adds that Freudenthal called Finns an inferior 'Turanian' people, a medal section tying the SFP's Freudenthal…
3. `sami-prehistory` #1 → `sami-prehistory/i01` campaign, shown as campaign: Adds paragraphs, cited to one magazine article, saying the Sámi are not more indigenous than the Finns, that …
4. `language-politics` #2 → `language-politics/i01` campaign, shown as contested: Adds a section 'Fennomania in the internet era': the term now means opponents of compulsory Swedish, some of …
5. `sami-prehistory` #3 → `sami-prehistory/i03` contested, shown as contested: Rewrites the etymology of 'kuningas' so the Germanic-loan origin is only 'claimed', argues the word was used …

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `sami-prehistory/i02` campaign: Turns the redirect 'Metsäsaamelaiset' into an article stating that the Forest Sámi are one of the (four) Sámi… (page 1096144, `sami-prehistory`)
2. **missed** `far-right/i02` contested: Recasts de Wit's 'Laiton lehti?' publication as a 'vulgar nationalist blog' and adds that he became known for… (page 1395607, `far-right`)
3. **missed** `far-right/i03` contested: Adds a 'Criticism' section saying Turtiainen's pro-Russia line would likely drop Finnish living standards to … (page 1105958, `far-right`)
4. **missed** `far-right/i06` contested: Adds a paragraph saying the Lapua movement's first meetings were in Swedish-speaking parishes, that its Swedi… (page 11477, `far-right`)
5. **missed** `far-right/i07` contested: Changes the SKE infobox title of its secretary from 'party secretary' to 'secretary', on the grounds that SKE… (page 1445320, `far-right`)

**Unlabelled:** 127 shown incident(s) have no golden item → `research/38-s11-decision/results/s10-b2-rel-coord3.test.unlabelled.jsonl` for the E3 judge.

**Failures:** none.

**Cost:** 13.3 s per request (max 14.7 s, 1 at a time), 239 LLM calls, 621,301 in / 92,541 out tokens, $2.14.

<details><summary>Command</summary>

```
research/eval/run signals --split test --name s10-b2-rel-coord3 -- python3 research/37-s10-assembly/variant.py --mode B+rel --assessments research/38-s11-decision/results/test-candidates.assess.carbon.jsonl --threshold min_coordination_signals=3
```

</details>
