### Eval scorecard · `s10-b-noew` · signals · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 10 topics, 10 requests · limit 50 · commit `6bb73162919d-dirty` · 2026-11-14 17:32:09 UTC · 0 failed

| Metric | Value | Δ vs `s3a-items` |
|---|---:|---:|
| strict_p@10 | 0.79 | +0.03 |
| strict_p@25 | 0.67 | +0.07 |
| lenient_p@10 | 0.98 | +0.03 |
| lenient_p@25 | 0.98 | +0.15 |
| unlabelled@10 | 6.11 | -0.78 |
| unlabelled@25 | 18.44 | -0.11 |
| recall | 0.58 | -0.04 |
| level_accuracy | 0.59 | +0.22 |
| overclaim_rate | 0.27 | +0.15 |
| duplicates | 0 | -10 |
| quiet_shown | 9 | -10 |
| quiet_contested_or_above | 1 | +1 |
| seconds_per_topic | 13.55 | +10.57 |
| llm_calls | 643 | +643 |
| input_tokens | 1,674,065 | +1,674,065 |
| output_tokens | 244,320 | +244,320 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `civil-war-1918` | topic | 45 | 0 | 5 | 4 | 1 | 0 | 35 | 0.60 | 5 / 10 | 15.8 |
| `continuation-war` | topic | 41 | 0 | 4 | 4 | 1 | 0 | 32 | 0.40 | 3 / 7 | 5.8 |
| `winter-war` | topic | 28 | 0 | 5 | 2 | 0 | 0 | 21 | 1.00 | 5 / 5 | 40.5 |
| `ceded-karelia` | topic | 50 | 0 | 7 | 3 | 0 | 0 | 40 | 0.60 | 7 / 10 | 5.0 |
| `nato-debate` | topic | 47 | 1 | 7 | 2 | 2 | 0 | 35 | 1.00 | 8 / 12 | 8.6 |
| `finlandization` | topic | 50 | 0 | 3 | 1 | 1 | 0 | 45 | 1.00 | 3 / 7 | 19.0 |
| `austerity-politics` | topic | 50 | 0 | 4 | 1 | 0 | 0 | 45 | 1.00 | 4 / 11 | 20.6 |
| `covid-19-finland` | topic | 27 | 0 | 2 | 0 | 0 | 0 | 25 | 1.00 | 2 / 5 | 8.8 |
| `russification-independence` | topic | 33 | 0 | 4 | 4 | 1 | 0 | 24 | 0.50 | 4 / 5 | 6.2 |
| `border-crisis-2023` | control-quiet | 9 | 0 | 1 | 3 | 0 | 0 | 5 | 0.25 | 1 / 1 | 5.1 |

**5 best** (highest-ranked true positives)

1. `border-crisis-2023` #1 → `border-crisis-2023/i01` contested, shown as contested: Rewrites the border-history paragraphs: adds that the Soviet Union unilaterally annulled the peace treaty by …
2. `covid-19-finland` #1 → `austerity-politics/i09` contested, shown as contested: Adds that Marin attended the Bilderberg meeting in 2022 and 2023, that the meeting is suspected of planning a…
3. `nato-debate` #1 → `nato-debate/i05` contested, shown as campaign: Puts back a sourced passage saying an association (Kehittyvien maakuntien Suomi) gave Niinistö's 2007 campaig…
4. `nato-debate` #2 → `nato-debate/i01` campaign, shown as campaign: Adds or puts back an Afghanistan passage in the list of Finland's wars saying Finland takes part in war opera…
5. `russification-independence` #2 → `russification-independence/i01` contested, shown as contested: Adds passages saying the Jägers knowingly committed treason and 'took the path of a coup' against the lawful …

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `austerity-politics/i01` contested: Adds to Stubb's private-life section that his social-media use has annoyed citizens and that under his govern… (page 11396, `austerity-politics`)
2. **missed** `austerity-politics/i02` contested: Adds that Katainen's record as finance minister 2007-2011 caused the Finns Party's 2011 'jytky', voters signa… (page 10858, `austerity-politics`)
3. **missed** `austerity-politics/i11` contested: Adds that in November 2015 Stubb told parliamentary question time that 90% of expert statements on the nomine… (page 11396, `austerity-politics`)
4. **missed** `austerity-politics/i18` contested: Adds a 'Language questions' section saying Stubb did not arrange German interpretation at the start of the EU… (page 11396, `austerity-politics`)
5. **missed** `austerity-politics/i21` contested: Adds that during the 2007 Tehy nurses' strike Soininvaara wrote that if someone had to die because of the str… (page 9059, `austerity-politics`)

**Unlabelled:** 307 shown incident(s) have no golden item → `research/37-s10-assembly/results/s10-b-noew.dev.unlabelled.jsonl` for the E3 judge.

**Failures:** none.

**Cost:** 13.6 s per request (max 40.5 s, 1 at a time), 643 LLM calls, 1,674,065 in / 244,320 out tokens, $5.51.

<details><summary>Command</summary>

```
research/eval/run signals --name s10-b-noew -- python3 research/37-s10-assembly/variant.py --mode B --assessments research/37-s10-assembly/results/candidates.assess.carbon.jsonl --disable edit_war
```

</details>
