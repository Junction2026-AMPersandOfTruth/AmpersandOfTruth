### Eval scorecard · `s1-reverts` · signals · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 10 topics, 10 requests · limit 50 · commit `ebfdadc8bb6f` · 2026-11-14 14:37:00 UTC · 0 failed

| Metric | Value |
|---|---:|
| strict_p@10 | 0.44 |
| strict_p@25 | 0.42 |
| lenient_p@10 | 0.85 |
| lenient_p@25 | 0.70 |
| unlabelled@10 | 6.89 |
| unlabelled@25 | 17.33 |
| recall | 0.58 |
| level_accuracy | 0.38 |
| overclaim_rate | 0.13 |
| duplicates | 35 |
| quiet_shown | 26 |
| quiet_contested_or_above | 0 |
| seconds_per_topic | 6.17 |
| llm_calls | 0 |
| input_tokens | 0 |
| output_tokens | 0 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `civil-war-1918` | topic | 50 | 0 | 4 | 6 | 0 | 5 | 35 | 0.40 | 4 / 10 | 4.3 |
| `continuation-war` | topic | 50 | 0 | 5 | 6 | 2 | 7 | 30 | 0.17 | 5 / 7 | 4.3 |
| `winter-war` | topic | 50 | 0 | 4 | 6 | 0 | 7 | 33 | 0.50 | 4 / 5 | 5.1 |
| `ceded-karelia` | topic | 50 | 0 | 7 | 2 | 0 | 6 | 35 | – | 7 / 10 | 3.3 |
| `nato-debate` | topic | 50 | 1 | 7 | 1 | 2 | 2 | 37 | 0.50 | 8 / 12 | 5.4 |
| `finlandization` | topic | 50 | 0 | 1 | 0 | 0 | 0 | 49 | – | 1 / 7 | 6.9 |
| `austerity-politics` | topic | 50 | 0 | 4 | 1 | 2 | 1 | 42 | – | 4 / 11 | 10.1 |
| `covid-19-finland` | topic | 50 | 0 | 4 | 2 | 1 | 0 | 43 | 0.50 | 4 / 5 | 10.9 |
| `russification-independence` | topic | 50 | 0 | 4 | 4 | 2 | 5 | 35 | 0.57 | 4 / 5 | 7.8 |
| `border-crisis-2023` | control-quiet | 26 | 0 | 1 | 4 | 2 | 2 | 17 | 0.14 | 1 / 1 | 3.7 |

**5 best** (highest-ranked true positives)

1. `border-crisis-2023` #1 → `border-crisis-2023/i01` contested, shown as reverted: Rewrites the border-history paragraphs: adds that the Soviet Union unilaterally annulled the peace treaty by …
2. `civil-war-1918` #2 → `civil-war-1918/i06` contested, shown as reverted: Rewrites the lead and naming section: calls the Reds 'rebels', says the war 'turned out to be a war of libera…
3. `russification-independence` #2 → `russification-independence/i01` contested, shown as reverted: Adds passages saying the Jägers knowingly committed treason and 'took the path of a coup' against the lawful …
4. `nato-debate` #3 → `nato-debate/i01` campaign, shown as reverted: Adds or puts back an Afghanistan passage in the list of Finland's wars saying Finland takes part in war opera…
5. `winter-war` #3 → `winter-war/i04` contested, shown as reverted: Adds two paragraphs on Häyhä's kill count: a 1941 Hurtti Ukko interview (headline '600 kills for sure', 259 w…

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `austerity-politics/i01` contested: Adds to Stubb's private-life section that his social-media use has annoyed citizens and that under his govern… (page 11396, `austerity-politics`)
2. **missed** `austerity-politics/i02` contested: Adds that Katainen's record as finance minister 2007-2011 caused the Finns Party's 2011 'jytky', voters signa… (page 10858, `austerity-politics`)
3. **missed** `austerity-politics/i12` contested: Adds a section 'Statement on nuclear power' saying that in Soininvaara's view nuclear power fights the greenh… (page 9059, `austerity-politics`)
4. **missed** `austerity-politics/i18` contested: Adds a 'Language questions' section saying Stubb did not arrange German interpretation at the start of the EU… (page 11396, `austerity-politics`)
5. **missed** `austerity-politics/i21` contested: Adds that during the 2007 Tehy nurses' strike Soininvaara wrote that if someone had to die because of the str… (page 9059, `austerity-politics`)

**Unlabelled:** 356 shown incident(s) have no golden item → `research/28-s1-revert/results/s1-reverts.dev.unlabelled.jsonl` for the E3 judge.

**Failures:** none.

**Cost:** 6.2 s per request (max 10.9 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run signals --name s1-reverts -- python3 research/28-s1-revert/variant.py
```

</details>
