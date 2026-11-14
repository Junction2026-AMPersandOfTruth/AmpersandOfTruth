### Eval scorecard · `s3a-items` · signals · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 10 topics, 10 requests · limit 50 · commit `0151a62a1cb7` · 2026-11-14 15:56:10 UTC · 0 failed

| Metric | Value | Δ vs `s1-reverts` |
|---|---:|---:|
| strict_p@10 | 0.76 | +0.32 |
| strict_p@25 | 0.61 | +0.19 |
| lenient_p@10 | 0.95 | +0.10 |
| lenient_p@25 | 0.83 | +0.13 |
| unlabelled@10 | 6.89 | 0.00 |
| unlabelled@25 | 18.56 | +1.22 |
| recall | 0.62 | +0.04 |
| level_accuracy | 0.37 | -0.01 |
| overclaim_rate | 0.12 | -0.01 |
| duplicates | 10 | -25 |
| quiet_shown | 19 | -7 |
| quiet_contested_or_above | 0 | 0 |
| seconds_per_topic | 2.98 | -3.19 |
| llm_calls | 0 | 0 |
| input_tokens | 0 | 0 |
| output_tokens | 0 | 0 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `civil-war-1918` | topic | 50 | 0 | 6 | 6 | 0 | 0 | 38 | 0.50 | 6 / 10 | 2.8 |
| `continuation-war` | topic | 50 | 0 | 4 | 5 | 1 | 2 | 38 | 0.50 | 4 / 7 | 2.3 |
| `winter-war` | topic | 50 | 0 | 5 | 8 | 1 | 0 | 36 | 0.67 | 5 / 5 | 2.2 |
| `ceded-karelia` | topic | 50 | 0 | 6 | 2 | 0 | 3 | 39 | 1.00 | 6 / 10 | 2.4 |
| `nato-debate` | topic | 50 | 1 | 7 | 1 | 2 | 3 | 36 | 0.75 | 8 / 12 | 4.0 |
| `finlandization` | topic | 50 | 0 | 4 | 0 | 2 | 1 | 43 | 1.00 | 4 / 7 | 5.6 |
| `austerity-politics` | topic | 50 | 0 | 5 | 2 | 1 | 0 | 42 | 1.00 | 5 / 11 | 3.9 |
| `covid-19-finland` | topic | 50 | 0 | 2 | 1 | 1 | 0 | 46 | 1.00 | 2 / 5 | 2.2 |
| `russification-independence` | topic | 50 | 0 | 4 | 4 | 1 | 0 | 41 | 0.40 | 4 / 5 | 2.4 |
| `border-crisis-2023` | control-quiet | 19 | 0 | 1 | 4 | 2 | 1 | 11 | 0.14 | 1 / 1 | 2.2 |

**5 best** (highest-ranked true positives)

1. `winter-war` #1 → `winter-war/i04` contested, shown as reverted: Adds two paragraphs on Häyhä's kill count: a 1941 Hurtti Ukko interview (headline '600 kills for sure', 259 w…
2. `border-crisis-2023` #2 → `border-crisis-2023/i01` contested, shown as reverted: Rewrites the border-history paragraphs: adds that the Soviet Union unilaterally annulled the peace treaty by …
3. `ceded-karelia` #2 → `ceded-karelia/i09` contested, shown as reverted: Adds to the first sentence, before its Kotus reference, that Karelia lies between the Gulf of Finland, Saimaa…
4. `covid-19-finland` #2 → `austerity-politics/i04` contested, shown as reverted: Adds to the 2021 Marin breakfast-allowance controversy that former PM Jyrki Katainen has said he used a simil…
5. `nato-debate` #2 → `austerity-politics/i11` contested, shown as reverted: Adds that in November 2015 Stubb told parliamentary question time that 90% of expert statements on the nomine…

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `austerity-politics/i01` contested: Adds to Stubb's private-life section that his social-media use has annoyed citizens and that under his govern… (page 11396, `austerity-politics`)
2. **missed** `austerity-politics/i02` contested: Adds that Katainen's record as finance minister 2007-2011 caused the Finns Party's 2011 'jytky', voters signa… (page 10858, `austerity-politics`)
3. **missed** `austerity-politics/i09` contested: Adds that Marin attended the Bilderberg meeting in 2022 and 2023, that the meeting is suspected of planning a… (page 1121620, `austerity-politics`)
4. **missed** `austerity-politics/i21` contested: Adds that during the 2007 Tehy nurses' strike Soininvaara wrote that if someone had to die because of the str… (page 9059, `austerity-politics`)
5. **missed** `austerity-politics/i22` contested: Expands the August 2010 'Russia is not a great power' controversy with an IMF comparison of the Russian and D… (page 10858, `austerity-politics`)

**Unlabelled:** 370 shown incident(s) have no golden item → `research/30-s3-extract/results/s3a-items.dev.unlabelled.jsonl` for the E3 judge.

**Failures:** none.

**Cost:** 3.0 s per request (max 5.6 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run signals --name s3a-items -- python3 research/30-s3-extract/variant.py
```

</details>
