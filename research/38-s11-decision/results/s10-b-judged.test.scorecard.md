### Eval scorecard · `s10-b-judged` · signals · test split

Golden `2026-11-14.1` (`m-2026-10`) · 4 topics, 4 requests · limit 50 · commit `d5779f50a2ef-dirty` · 2026-11-14 19:34:51 UTC · 0 failed

| Metric | Value |
|---|---:|
| strict_p@10 | 0.85 |
| strict_p@25 | 0.53 |
| lenient_p@10 | 0.95 |
| lenient_p@25 | 0.93 |
| unlabelled@10 | 6.00 |
| unlabelled@25 | 18.75 |
| strict_p@10_judged | 0.77 |
| strict_p@25_judged | 0.55 |
| unjudged@10 | 0 |
| unjudged@25 | 1 |
| recall | 0.54 |
| level_accuracy | 0.54 |
| overclaim_rate | 0.38 |
| duplicates | 1 |
| quiet_shown | 0 |
| quiet_contested_or_above | 0 |
| seconds_per_topic | 13.46 |
| llm_calls | 230 |
| input_tokens | 586,886 |
| output_tokens | 88,399 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | judged p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `language-politics` | topic | 26 | 1 | 2 | 2 | 0 | 0 | 21 | 1.00 | 0.80 | 3 / 7 | 12.3 |
| `sami-prehistory` | topic | 40 | 1 | 3 | 2 | 0 | 0 | 34 | 0.80 | 0.80 | 4 / 9 | 12.0 |
| `far-right` | topic | 50 | 1 | 3 | 3 | 0 | 1 | 42 | 0.60 | 0.70 | 4 / 7 | 14.9 |
| `national-myths` | topic | 38 | 1 | 2 | 3 | 2 | 0 | 30 | 1.00 | 0.80 | 3 / 3 | 14.7 |

**5 best** (highest-ranked true positives)

1. `far-right` #1 → `far-right/i04` contested, shown as campaign: Rewrites the IKL lead as a 'far-right fascist party that took many influences from German National Socialism …
2. `language-politics` #1 → `language-politics/i01` campaign, shown as campaign: Adds a section 'Fennomania in the internet era': the term now means opponents of compulsory Swedish, some of …
3. `sami-prehistory` #1 → `sami-prehistory/i01` campaign, shown as campaign: Adds paragraphs, cited to one magazine article, saying the Sámi are not more indigenous than the Finns, that …
4. `sami-prehistory` #3 → `sami-prehistory/i03` contested, shown as contested: Rewrites the etymology of 'kuningas' so the Germanic-loan origin is only 'claimed', argues the word was used …
5. `far-right` #4 → `far-right/i05` contested, shown as campaign: Replaces the SKE board in the infobox with the rival board elected in 2019 and adds a signed note that the PR…

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `sami-prehistory/i02` campaign: Turns the redirect 'Metsäsaamelaiset' into an article stating that the Forest Sámi are one of the (four) Sámi… (page 1096144, `sami-prehistory`)
2. **missed** `far-right/i02` contested: Recasts de Wit's 'Laiton lehti?' publication as a 'vulgar nationalist blog' and adds that he became known for… (page 1395607, `far-right`)
3. **missed** `far-right/i06` contested: Adds a paragraph saying the Lapua movement's first meetings were in Swedish-speaking parishes, that its Swedi… (page 11477, `far-right`)
4. **missed** `far-right/i07` contested: Changes the SKE infobox title of its secretary from 'party secretary' to 'secretary', on the grounds that SKE… (page 1445320, `far-right`)
5. **missed** `language-politics/i02` contested: Adds that Freudenthal called Finns an inferior 'Turanian' people, a medal section tying the SFP's Freudenthal… (page 200797, `language-politics`)

**Unlabelled:** 127 shown incident(s) have no golden item → `research/38-s11-decision/results/s10-b-judged.test.unlabelled.jsonl` for the E3 judge.
Judged precision counts an unjudged hit as a miss: 0 unjudged in the top 10, 1 in the top 25 (all topics).

**Failures:** none.

**Cost:** 13.5 s per request (max 14.9 s, 1 at a time), 230 LLM calls, 586,886 in / 88,399 out tokens, $2.03.

<details><summary>Command</summary>

```
research/eval/run signals --split test --name s10-b -- python3 research/37-s10-assembly/variant.py --mode B --sides off --edit-war-source s4 --assessments research/38-s11-decision/results/test-candidates.assess.carbon.jsonl
```

</details>
