### Eval scorecard · `s3a-items-judged` · signals · test split

Golden `2026-11-14.1` (`m-2026-10`) · 4 topics, 4 requests · limit 50 · commit `d5779f50a2ef-dirty` · 2026-11-14 19:34:39 UTC · 0 failed

| Metric | Value |
|---|---:|
| strict_p@10 | 0.56 |
| strict_p@25 | 0.46 |
| lenient_p@10 | 0.77 |
| lenient_p@25 | 0.63 |
| unlabelled@10 | 5.00 |
| unlabelled@25 | 16.25 |
| strict_p@10_judged | 0.62 |
| strict_p@25_judged | 0.54 |
| unjudged@10 | 1 |
| unjudged@25 | 1 |
| recall | 0.62 |
| level_accuracy | 0.33 |
| overclaim_rate | 0.07 |
| duplicates | 15 |
| quiet_shown | 0 |
| quiet_contested_or_above | 0 |
| seconds_per_topic | 2.02 |
| llm_calls | 0 |
| input_tokens | 0 |
| output_tokens | 0 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | judged p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `language-politics` | topic | 50 | 1 | 4 | 1 | 1 | 6 | 37 | 0.50 | 0.50 | 5 / 7 | 2.5 |
| `sami-prehistory` | topic | 50 | 1 | 4 | 2 | 0 | 3 | 40 | 0.75 | 0.70 | 5 / 9 | 1.6 |
| `far-right` | topic | 50 | 1 | 2 | 3 | 0 | 4 | 40 | 0.50 | 0.60 | 3 / 7 | 2.0 |
| `national-myths` | topic | 50 | 1 | 2 | 3 | 1 | 2 | 41 | 0.50 | 0.70 | 3 / 3 | 1.9 |

**5 best** (highest-ranked true positives)

1. `language-politics` #1 → `language-politics/i01` campaign, shown as reverted: Adds a section 'Fennomania in the internet era': the term now means opponents of compulsory Swedish, some of …
2. `national-myths` #2 → `national-myths/i02` contested, shown as reverted: Adds to 'Kalevalan synty' that epic poetry is found almost everywhere, has often been compiled into written e…
3. `sami-prehistory` #2 → `sami-prehistory/i03` contested, shown as reverted: Rewrites the etymology of 'kuningas' so the Germanic-loan origin is only 'claimed', argues the word was used …
4. `sami-prehistory` #3 → `sami-prehistory/i01` campaign, shown as reverted: Adds paragraphs, cited to one magazine article, saying the Sámi are not more indigenous than the Finns, that …
5. `language-politics` #6 → `language-politics/i06` contested, shown as reverted: Adds a paragraph saying Freudenthal held racist ideas about Finns and believed Finnish speakers were racially…

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `sami-prehistory/i02` campaign: Turns the redirect 'Metsäsaamelaiset' into an article stating that the Forest Sámi are one of the (four) Sámi… (page 1096144, `sami-prehistory`)
2. **missed** `far-right/i02` contested: Recasts de Wit's 'Laiton lehti?' publication as a 'vulgar nationalist blog' and adds that he became known for… (page 1395607, `far-right`)
3. **missed** `far-right/i03` contested: Adds a 'Criticism' section saying Turtiainen's pro-Russia line would likely drop Finnish living standards to … (page 1105958, `far-right`)
4. **missed** `far-right/i06` contested: Adds a paragraph saying the Lapua movement's first meetings were in Swedish-speaking parishes, that its Swedi… (page 11477, `far-right`)
5. **missed** `far-right/i07` contested: Changes the SKE infobox title of its secretary from 'party secretary' to 'secretary', on the grounds that SKE… (page 1445320, `far-right`)

**Unlabelled:** 158 shown incident(s) have no golden item → `research/38-s11-decision/results/s3a-items-judged.test.unlabelled.jsonl` for the E3 judge.
Judged precision counts an unjudged hit as a miss: 1 unjudged in the top 10, 1 in the top 25 (all topics).

**Failures:** none.

**Cost:** 2.0 s per request (max 2.5 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run signals --split test --name s3a-items -- python3 research/30-s3-extract/variant.py
```

</details>
