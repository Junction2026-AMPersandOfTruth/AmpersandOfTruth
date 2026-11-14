### Eval scorecard · `s1-reverts-judged` · signals · test split

Golden `2026-11-14.1` (`m-2026-10`) · 4 topics, 4 requests · limit 50 · commit `d5779f50a2ef-dirty` · 2026-11-14 19:34:30 UTC · 0 failed

| Metric | Value |
|---|---:|
| strict_p@10 | 0.23 |
| strict_p@25 | 0.25 |
| lenient_p@10 | 0.58 |
| lenient_p@25 | 0.43 |
| unlabelled@10 | 6.25 |
| unlabelled@25 | 15.25 |
| strict_p@10_judged | 0.25 |
| strict_p@25_judged | 0.32 |
| unjudged@10 | 0 |
| unjudged@25 | 0 |
| recall | 0.65 |
| level_accuracy | 0.33 |
| overclaim_rate | 0.15 |
| duplicates | 30 |
| quiet_shown | 0 |
| quiet_contested_or_above | 0 |
| seconds_per_topic | 2.29 |
| llm_calls | 0 |
| input_tokens | 0 |
| output_tokens | 0 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | judged p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `language-politics` | topic | 50 | 1 | 4 | 1 | 1 | 15 | 28 | 0.00 | 0.10 | 5 / 7 | 2.0 |
| `sami-prehistory` | topic | 50 | 1 | 4 | 2 | 0 | 5 | 38 | 0.60 | 0.50 | 5 / 9 | 2.2 |
| `far-right` | topic | 50 | 1 | 3 | 5 | 0 | 6 | 35 | 0.00 | 0.10 | 4 / 7 | 2.5 |
| `national-myths` | topic | 50 | 1 | 2 | 3 | 4 | 4 | 36 | 0.33 | 0.30 | 3 / 3 | 2.4 |

**5 best** (highest-ranked true positives)

1. `sami-prehistory` #3 → `sami-prehistory/i01` campaign, shown as reverted: Adds paragraphs, cited to one magazine article, saying the Sámi are not more indigenous than the Finns, that …
2. `sami-prehistory` #6 → `sami-prehistory/i04` contested, shown as reverted: Adds a section saying Finland's history was distorted and suppressed by conquerors (including a death penalty…
3. `sami-prehistory` #8 → `sami-prehistory/i05` contested, shown as reverted: Adds a 'genetic research' section saying genetic studies point to Finno-Ugric roots of historical rulers and …
4. `national-myths` #9 → `national-myths/i02` contested, shown as reverted: Adds to 'Kalevalan synty' that epic poetry is found almost everywhere, has often been compiled into written e…
5. `national-myths` #12 → `language-politics/i01` campaign, shown as reverted: Adds a section 'Fennomania in the internet era': the term now means opponents of compulsory Swedish, some of …

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `sami-prehistory/i02` campaign: Turns the redirect 'Metsäsaamelaiset' into an article stating that the Forest Sámi are one of the (four) Sámi… (page 1096144, `sami-prehistory`)
2. **missed** `far-right/i03` contested: Adds a 'Criticism' section saying Turtiainen's pro-Russia line would likely drop Finnish living standards to … (page 1105958, `far-right`)
3. **missed** `far-right/i05` contested: Replaces the SKE board in the infobox with the rival board elected in 2019 and adds a signed note that the PR… (page 1445320, `far-right`)
4. **missed** `far-right/i06` contested: Adds a paragraph saying the Lapua movement's first meetings were in Swedish-speaking parishes, that its Swedi… (page 11477, `far-right`)
5. **missed** `language-politics/i03` contested: Adds to the Rasismi section that the defenders of Finnish 'did not resort to racism', citing the pro-Finnish … (page 200797, `language-politics`)

**Unlabelled:** 137 shown incident(s) have no golden item → `research/38-s11-decision/results/s1-reverts-judged.test.unlabelled.jsonl` for the E3 judge.
Judged precision counts an unjudged hit as a miss: 0 unjudged in the top 10, 0 in the top 25 (all topics).

**Failures:** none.

**Cost:** 2.3 s per request (max 2.5 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run signals --split test --name s1-reverts -- python3 research/28-s1-revert/variant.py
```

</details>
