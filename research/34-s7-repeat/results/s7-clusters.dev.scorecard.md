### Eval scorecard · `s7-clusters` · signals · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 10 topics, 10 requests · limit 50 · commit `0151a62a1cb7` · 2026-11-14 15:46:29 UTC · 0 failed

| Metric | Value | Δ vs `s1-reverts` |
|---|---:|---:|
| strict_p@10 | 0.68 | +0.24 |
| strict_p@25 | 0.65 | +0.23 |
| lenient_p@10 | 0.90 | +0.06 |
| lenient_p@25 | 0.93 | +0.23 |
| unlabelled@10 | 6.78 | -0.11 |
| unlabelled@25 | 18.89 | +1.56 |
| recall | 0.55 | -0.03 |
| level_accuracy | 0.41 | +0.03 |
| overclaim_rate | 0.07 | -0.06 |
| duplicates | 1 | -34 |
| quiet_shown | 11 | -15 |
| quiet_contested_or_above | 0 | 0 |
| seconds_per_topic | 1.99 | -4.18 |
| llm_calls | 0 | 0 |
| input_tokens | 0 | 0 |
| output_tokens | 0 | 0 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `civil-war-1918` | topic | 50 | 0 | 4 | 5 | 0 | 0 | 41 | 0.50 | 4 / 10 | 1.6 |
| `continuation-war` | topic | 50 | 0 | 5 | 5 | 1 | 0 | 39 | 0.17 | 4 / 7 | 1.1 |
| `winter-war` | topic | 50 | 0 | 4 | 4 | 2 | 0 | 40 | 0.75 | 4 / 5 | 1.2 |
| `ceded-karelia` | topic | 50 | 0 | 6 | 2 | 0 | 1 | 41 | 0.80 | 6 / 10 | 1.4 |
| `nato-debate` | topic | 50 | 1 | 6 | 2 | 0 | 0 | 41 | 1.00 | 7 / 12 | 2.7 |
| `finlandization` | topic | 50 | 0 | 3 | 0 | 0 | 0 | 47 | 1.00 | 2 / 7 | 3.2 |
| `austerity-politics` | topic | 50 | 0 | 4 | 3 | 0 | 0 | 43 | 1.00 | 4 / 11 | 1.2 |
| `covid-19-finland` | topic | 50 | 0 | 4 | 3 | 1 | 0 | 42 | 0.50 | 4 / 5 | 1.2 |
| `russification-independence` | topic | 50 | 0 | 4 | 5 | 2 | 0 | 39 | 0.40 | 4 / 5 | 4.5 |
| `border-crisis-2023` | control-quiet | 11 | 0 | 1 | 4 | 0 | 0 | 6 | 0.20 | 1 / 1 | 1.7 |

**5 best** (highest-ranked true positives)

1. `austerity-politics` #1 → `austerity-politics/i18` contested, shown as reverted: Adds a 'Language questions' section saying Stubb did not arrange German interpretation at the start of the EU…
2. `border-crisis-2023` #1 → `border-crisis-2023/i01` contested, shown as reverted: Rewrites the border-history paragraphs: adds that the Soviet Union unilaterally annulled the peace treaty by …
3. `nato-debate` #1 → `nato-debate/i05` contested, shown as reverted: Puts back a sourced passage saying an association (Kehittyvien maakuntien Suomi) gave Niinistö's 2007 campaig…
4. `continuation-war` #2 → `civil-war-1918/i10` contested, shown as reverted: Changes the presidency end date to 11.3.1946 and regency end to 26.7.1919 with kysy.fi and presidentti.fi sou…
5. `ceded-karelia` #3 → `ceded-karelia/i10` contested, shown as reverted: Adds a 'motive of Greater Finland' passage: supporters wanted to save kindred peoples from ethnic cleansing o…

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `austerity-politics/i01` contested: Adds to Stubb's private-life section that his social-media use has annoyed citizens and that under his govern… (page 11396, `austerity-politics`)
2. **missed** `austerity-politics/i02` contested: Adds that Katainen's record as finance minister 2007-2011 caused the Finns Party's 2011 'jytky', voters signa… (page 10858, `austerity-politics`)
3. **missed** `austerity-politics/i04` contested: Adds to the 2021 Marin breakfast-allowance controversy that former PM Jyrki Katainen has said he used a simil… (page 1121620, `austerity-politics`)
4. **missed** `austerity-politics/i11` contested: Adds that in November 2015 Stubb told parliamentary question time that 90% of expert statements on the nomine… (page 11396, `austerity-politics`)
5. **missed** `austerity-politics/i21` contested: Adds that during the 2007 Tehy nurses' strike Soininvaara wrote that if someone had to die because of the str… (page 9059, `austerity-politics`)

**Unlabelled:** 379 shown incident(s) have no golden item → `research/34-s7-repeat/results/s7-clusters.dev.unlabelled.jsonl` for the E3 judge.

**Failures:** none.

**Cost:** 2.0 s per request (max 4.5 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run signals --name s7-clusters -- python3 research/34-s7-repeat/variant.py
```

</details>
