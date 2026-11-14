### Eval scorecard · `s10-b-noassess` · signals · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 10 topics, 10 requests · limit 50 · commit `c38623e4003a` · 2026-11-14 17:10:11 UTC · 0 failed

| Metric | Value | Δ vs `s7-clusters` |
|---|---:|---:|
| strict_p@10 | 0.68 | 0.00 |
| strict_p@25 | 0.61 | -0.04 |
| lenient_p@10 | 0.92 | +0.02 |
| lenient_p@25 | 0.93 | -0.01 |
| unlabelled@10 | 6.00 | -0.78 |
| unlabelled@25 | 17.78 | -1.11 |
| recall | 0.58 | +0.03 |
| level_accuracy | 0.34 | -0.07 |
| overclaim_rate | 0.12 | +0.05 |
| duplicates | 0 | -1 |
| quiet_shown | 9 | -2 |
| quiet_contested_or_above | 0 | 0 |
| seconds_per_topic | 7.33 | +5.34 |
| llm_calls | 0 | 0 |
| input_tokens | 0 | 0 |
| output_tokens | 0 | 0 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `civil-war-1918` | topic | 47 | 0 | 5 | 4 | 1 | 0 | 37 | 0.67 | 5 / 10 | 10.4 |
| `continuation-war` | topic | 45 | 0 | 4 | 5 | 1 | 0 | 35 | 0.29 | 3 / 7 | 5.2 |
| `winter-war` | topic | 37 | 0 | 4 | 4 | 1 | 0 | 28 | 0.40 | 4 / 5 | 7.3 |
| `ceded-karelia` | topic | 50 | 0 | 7 | 3 | 0 | 0 | 40 | 0.80 | 7 / 10 | 5.4 |
| `nato-debate` | topic | 47 | 1 | 8 | 2 | 2 | 0 | 34 | 0.80 | 9 / 12 | 8.6 |
| `finlandization` | topic | 50 | 0 | 4 | 0 | 1 | 0 | 45 | 1.00 | 3 / 7 | 13.8 |
| `austerity-politics` | topic | 50 | 0 | 5 | 2 | 0 | 0 | 43 | 1.00 | 5 / 11 | 9.9 |
| `covid-19-finland` | topic | 29 | 0 | 2 | 0 | 1 | 0 | 26 | 0.67 | 2 / 5 | 4.0 |
| `russification-independence` | topic | 50 | 0 | 3 | 5 | 3 | 0 | 39 | 0.50 | 3 / 5 | 4.9 |
| `border-crisis-2023` | control-quiet | 9 | 0 | 1 | 3 | 0 | 0 | 5 | 0.25 | 1 / 1 | 3.7 |

**5 best** (highest-ranked true positives)

1. `austerity-politics` #1 → `austerity-politics/i18` contested, shown as reverted: Adds a 'Language questions' section saying Stubb did not arrange German interpretation at the start of the EU…
2. `border-crisis-2023` #1 → `border-crisis-2023/i01` contested, shown as reverted: Rewrites the border-history paragraphs: adds that the Soviet Union unilaterally annulled the peace treaty by …
3. `nato-debate` #2 → `nato-debate/i05` contested, shown as reverted: Puts back a sourced passage saying an association (Kehittyvien maakuntien Suomi) gave Niinistö's 2007 campaig…
4. `continuation-war` #3 → `civil-war-1918/i10` contested, shown as reverted: Changes the presidency end date to 11.3.1946 and regency end to 26.7.1919 with kysy.fi and presidentti.fi sou…
5. `covid-19-finland` #3 → `austerity-politics/i09` contested, shown as reverted: Adds that Marin attended the Bilderberg meeting in 2022 and 2023, that the meeting is suspected of planning a…

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `austerity-politics/i01` contested: Adds to Stubb's private-life section that his social-media use has annoyed citizens and that under his govern… (page 11396, `austerity-politics`)
2. **missed** `austerity-politics/i02` contested: Adds that Katainen's record as finance minister 2007-2011 caused the Finns Party's 2011 'jytky', voters signa… (page 10858, `austerity-politics`)
3. **missed** `austerity-politics/i11` contested: Adds that in November 2015 Stubb told parliamentary question time that 90% of expert statements on the nomine… (page 11396, `austerity-politics`)
4. **missed** `austerity-politics/i21` contested: Adds that during the 2007 Tehy nurses' strike Soininvaara wrote that if someone had to die because of the str… (page 9059, `austerity-politics`)
5. **missed** `austerity-politics/i22` contested: Expands the August 2010 'Russia is not a great power' controversy with an IMF comparison of the Russian and D… (page 10858, `austerity-politics`)

**Unlabelled:** 332 shown incident(s) have no golden item → `research/37-s10-assembly/results/s10-b-noassess.dev.unlabelled.jsonl` for the E3 judge.

**Failures:** none.

**Cost:** 7.3 s per request (max 13.8 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run signals --name s10-b-noassess -- python3 research/37-s10-assembly/variant.py --mode B
```

</details>
