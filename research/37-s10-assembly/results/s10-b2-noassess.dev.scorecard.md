### Eval scorecard · `s10-b2-noassess` · signals · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 10 topics, 10 requests · limit 50 · commit `f985802eb562-dirty` · 2026-11-14 18:29:17 UTC · 0 failed

| Metric | Value | Δ vs `s10-b` |
|---|---:|---:|
| strict_p@10 | 0.68 | -0.11 |
| strict_p@25 | 0.63 | -0.05 |
| lenient_p@10 | 0.95 | -0.03 |
| lenient_p@25 | 0.93 | -0.02 |
| unlabelled@10 | 6.11 | +0.11 |
| unlabelled@25 | 18.00 | +0.33 |
| recall | 0.58 | -0.04 |
| level_accuracy | 0.33 | -0.34 |
| overclaim_rate | 0.10 | -0.16 |
| duplicates | 0 | 0 |
| quiet_shown | 9 | 0 |
| quiet_contested_or_above | 0 | -2 |
| seconds_per_topic | 7.22 | -7.01 |
| llm_calls | 0 | -643 |
| input_tokens | 0 | -1,674,065 |
| output_tokens | 0 | -244,320 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `civil-war-1918` | topic | 47 | 0 | 5 | 3 | 1 | 0 | 38 | 0.67 | 5 / 10 | 8.7 |
| `continuation-war` | topic | 45 | 0 | 4 | 5 | 1 | 0 | 35 | 0.29 | 3 / 7 | 5.5 |
| `winter-war` | topic | 37 | 0 | 4 | 4 | 1 | 0 | 28 | 0.40 | 4 / 5 | 9.9 |
| `ceded-karelia` | topic | 50 | 0 | 7 | 3 | 0 | 0 | 40 | 0.80 | 7 / 10 | 3.9 |
| `nato-debate` | topic | 48 | 1 | 8 | 2 | 1 | 0 | 36 | 1.00 | 9 / 12 | 9.1 |
| `finlandization` | topic | 50 | 0 | 4 | 0 | 0 | 0 | 46 | 1.00 | 3 / 7 | 15.6 |
| `austerity-politics` | topic | 50 | 0 | 5 | 1 | 0 | 0 | 44 | 1.00 | 5 / 11 | 7.1 |
| `covid-19-finland` | topic | 29 | 0 | 2 | 0 | 1 | 0 | 26 | 0.67 | 2 / 5 | 4.3 |
| `russification-independence` | topic | 50 | 0 | 3 | 5 | 3 | 0 | 39 | 0.33 | 3 / 5 | 5.1 |
| `border-crisis-2023` | control-quiet | 9 | 0 | 1 | 3 | 0 | 0 | 5 | 0.25 | 1 / 1 | 3.0 |

**5 best** (highest-ranked true positives)

1. `austerity-politics` #1 → `austerity-politics/i18` contested, shown as reverted: Adds a 'Language questions' section saying Stubb did not arrange German interpretation at the start of the EU…
2. `border-crisis-2023` #1 → `border-crisis-2023/i01` contested, shown as reverted: Rewrites the border-history paragraphs: adds that the Soviet Union unilaterally annulled the peace treaty by …
3. `continuation-war` #2 → `civil-war-1918/i10` contested, shown as reverted: Changes the presidency end date to 11.3.1946 and regency end to 26.7.1919 with kysy.fi and presidentti.fi sou…
4. `covid-19-finland` #2 → `austerity-politics/i09` contested, shown as reverted: Adds that Marin attended the Bilderberg meeting in 2022 and 2023, that the meeting is suspected of planning a…
5. `nato-debate` #2 → `nato-debate/i05` contested, shown as reverted: Puts back a sourced passage saying an association (Kehittyvien maakuntien Suomi) gave Niinistö's 2007 campaig…

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `austerity-politics/i01` contested: Adds to Stubb's private-life section that his social-media use has annoyed citizens and that under his govern… (page 11396, `austerity-politics`)
2. **missed** `austerity-politics/i02` contested: Adds that Katainen's record as finance minister 2007-2011 caused the Finns Party's 2011 'jytky', voters signa… (page 10858, `austerity-politics`)
3. **missed** `austerity-politics/i11` contested: Adds that in November 2015 Stubb told parliamentary question time that 90% of expert statements on the nomine… (page 11396, `austerity-politics`)
4. **missed** `austerity-politics/i21` contested: Adds that during the 2007 Tehy nurses' strike Soininvaara wrote that if someone had to die because of the str… (page 9059, `austerity-politics`)
5. **missed** `austerity-politics/i22` contested: Expands the August 2010 'Russia is not a great power' controversy with an IMF comparison of the Russian and D… (page 10858, `austerity-politics`)

**Unlabelled:** 337 shown incident(s) have no golden item → `research/37-s10-assembly/results/s10-b2-noassess.dev.unlabelled.jsonl` for the E3 judge.

**Failures:** none.

**Cost:** 7.2 s per request (max 15.6 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run signals --name s10-b2-noassess -- python3 research/37-s10-assembly/variant.py --mode B --assessments research/37-s10-assembly/results/candidates.assess.carbon.jsonl --disable assessment
```

</details>
