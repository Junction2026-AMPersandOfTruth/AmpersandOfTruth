### Eval scorecard · `s10-b2-noreinsert` · signals · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 10 topics, 10 requests · limit 50 · commit `f985802eb562-dirty` · 2026-11-14 18:16:44 UTC · 0 failed

| Metric | Value | Δ vs `s10-b` |
|---|---:|---:|
| strict_p@10 | 0.84 | +0.06 |
| strict_p@25 | 0.72 | +0.04 |
| lenient_p@10 | 0.98 | 0.00 |
| lenient_p@25 | 0.97 | +0.03 |
| unlabelled@10 | 6.22 | +0.22 |
| unlabelled@25 | 18.56 | +0.89 |
| recall | 0.58 | -0.04 |
| level_accuracy | 0.60 | -0.07 |
| overclaim_rate | 0.25 | -0.02 |
| duplicates | 0 | 0 |
| quiet_shown | 9 | 0 |
| quiet_contested_or_above | 1 | -1 |
| seconds_per_topic | 20.97 | +6.74 |
| llm_calls | 643 | 0 |
| input_tokens | 1,674,065 | 0 |
| output_tokens | 244,320 | 0 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `civil-war-1918` | topic | 45 | 0 | 5 | 4 | 1 | 0 | 35 | 0.60 | 5 / 10 | 23.8 |
| `continuation-war` | topic | 41 | 0 | 4 | 4 | 1 | 0 | 32 | 0.40 | 3 / 7 | 15.0 |
| `winter-war` | topic | 28 | 0 | 5 | 2 | 0 | 0 | 21 | 1.00 | 5 / 5 | 14.7 |
| `ceded-karelia` | topic | 50 | 0 | 7 | 3 | 0 | 0 | 40 | 1.00 | 7 / 10 | 11.6 |
| `nato-debate` | topic | 48 | 1 | 7 | 2 | 2 | 0 | 36 | 1.00 | 8 / 12 | 22.9 |
| `finlandization` | topic | 50 | 0 | 3 | 1 | 1 | 0 | 45 | 1.00 | 3 / 7 | 33.6 |
| `austerity-politics` | topic | 50 | 0 | 4 | 1 | 0 | 0 | 45 | 1.00 | 4 / 11 | 26.4 |
| `covid-19-finland` | topic | 27 | 0 | 2 | 0 | 0 | 0 | 25 | 1.00 | 2 / 5 | 19.3 |
| `russification-independence` | topic | 33 | 0 | 4 | 4 | 1 | 0 | 24 | 0.60 | 4 / 5 | 20.7 |
| `border-crisis-2023` | control-quiet | 9 | 0 | 1 | 3 | 0 | 0 | 5 | 0.25 | 1 / 1 | 21.7 |

**5 best** (highest-ranked true positives)

1. `border-crisis-2023` #1 → `border-crisis-2023/i01` contested, shown as contested: Rewrites the border-history paragraphs: adds that the Soviet Union unilaterally annulled the peace treaty by …
2. `continuation-war` #1 → `civil-war-1918/i10` contested, shown as campaign: Changes the presidency end date to 11.3.1946 and regency end to 26.7.1919 with kysy.fi and presidentti.fi sou…
3. `covid-19-finland` #1 → `austerity-politics/i09` contested, shown as contested: Adds that Marin attended the Bilderberg meeting in 2022 and 2023, that the meeting is suspected of planning a…
4. `nato-debate` #1 → `nato-debate/i05` contested, shown as campaign: Puts back a sourced passage saying an association (Kehittyvien maakuntien Suomi) gave Niinistö's 2007 campaig…
5. `nato-debate` #2 → `nato-debate/i01` campaign, shown as contested: Adds or puts back an Afghanistan passage in the list of Finland's wars saying Finland takes part in war opera…

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `austerity-politics/i01` contested: Adds to Stubb's private-life section that his social-media use has annoyed citizens and that under his govern… (page 11396, `austerity-politics`)
2. **missed** `austerity-politics/i02` contested: Adds that Katainen's record as finance minister 2007-2011 caused the Finns Party's 2011 'jytky', voters signa… (page 10858, `austerity-politics`)
3. **missed** `austerity-politics/i11` contested: Adds that in November 2015 Stubb told parliamentary question time that 90% of expert statements on the nomine… (page 11396, `austerity-politics`)
4. **missed** `austerity-politics/i18` contested: Adds a 'Language questions' section saying Stubb did not arrange German interpretation at the start of the EU… (page 11396, `austerity-politics`)
5. **missed** `austerity-politics/i21` contested: Adds that during the 2007 Tehy nurses' strike Soininvaara wrote that if someone had to die because of the str… (page 9059, `austerity-politics`)

**Unlabelled:** 308 shown incident(s) have no golden item → `research/37-s10-assembly/results/s10-b2-noreinsert.dev.unlabelled.jsonl` for the E3 judge.

**Failures:** none.

**Cost:** 21.0 s per request (max 33.6 s, 1 at a time), 643 LLM calls, 1,674,065 in / 244,320 out tokens, $5.51.

<details><summary>Command</summary>

```
research/eval/run signals --name s10-b2-noreinsert -- python3 research/37-s10-assembly/variant.py --mode B --assessments research/37-s10-assembly/results/candidates.assess.carbon.jsonl --disable reinsertion
```

</details>
