signals variant 's1-reverts-v2': 10 request(s) on the dev split of golden 2026-11-14.1
[1/10] civil-war-1918: 1.0 s, ok
[2/10] continuation-war: 0.9 s, ok
[3/10] winter-war: 1.0 s, ok
[4/10] ceded-karelia: 1.3 s, ok
[5/10] nato-debate: 3.2 s, ok
[6/10] finlandization: 4.7 s, ok
[7/10] austerity-politics: 1.6 s, ok
[8/10] covid-19-finland: 1.0 s, ok
[9/10] russification-independence: 1.4 s, ok
[10/10] border-crisis-2023: 2.6 s, ok
wrote research/28-s1-revert/results/s1-reverts-v2.dev.scorecard.json (+ .md, .unlabelled.jsonl)
### Eval scorecard · `s1-reverts-v2` · signals · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 10 topics, 10 requests · limit 50 · commit `f4bb08596ee6` · 2026-11-14 15:55:38 UTC · 0 failed

| Metric | Value | Δ vs `s1-reverts` |
|---|---:|---:|
| strict_p@10 | 0.41 | -0.03 |
| strict_p@25 | 0.41 | -0.01 |
| lenient_p@10 | 0.82 | -0.02 |
| lenient_p@25 | 0.64 | -0.06 |
| unlabelled@10 | 7.11 | +0.22 |
| unlabelled@25 | 17.89 | +0.56 |
| recall | 0.53 | -0.04 |
| level_accuracy | 0.36 | -0.01 |
| overclaim_rate | 0.15 | +0.02 |
| duplicates | 34 | -1 |
| quiet_shown | 26 | 0 |
| quiet_contested_or_above | 0 | 0 |
| seconds_per_topic | 1.87 | -4.30 |
| llm_calls | 0 | 0 |
| input_tokens | 0 | 0 |
| output_tokens | 0 | 0 |

**Per topic** (outcomes of the shown incidents)

| Topic | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 | Found / expected | s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `civil-war-1918` | topic | 50 | 0 | 3 | 5 | 0 | 5 | 37 | 0.50 | 3 / 10 | 1.0 |
| `continuation-war` | topic | 50 | 0 | 5 | 5 | 2 | 7 | 31 | 0.14 | 5 / 7 | 0.9 |
| `winter-war` | topic | 50 | 0 | 4 | 5 | 0 | 6 | 35 | 0.25 | 4 / 5 | 1.0 |
| `ceded-karelia` | topic | 50 | 0 | 7 | 2 | 0 | 5 | 36 | – | 7 / 10 | 1.3 |
| `nato-debate` | topic | 50 | 1 | 5 | 1 | 2 | 2 | 39 | 0.50 | 6 / 12 | 3.2 |
| `finlandization` | topic | 50 | 0 | 1 | 0 | 1 | 3 | 45 | – | 1 / 7 | 4.7 |
| `austerity-politics` | topic | 50 | 0 | 4 | 1 | 2 | 0 | 43 | – | 4 / 11 | 1.6 |
| `covid-19-finland` | topic | 50 | 0 | 4 | 2 | 0 | 0 | 44 | 0.50 | 4 / 5 | 1.0 |
| `russification-independence` | topic | 50 | 0 | 4 | 4 | 3 | 4 | 35 | 0.57 | 4 / 5 | 1.4 |
| `border-crisis-2023` | control-quiet | 26 | 0 | 1 | 4 | 2 | 2 | 17 | 0.14 | 1 / 1 | 2.6 |

**5 best** (highest-ranked true positives)

1. `border-crisis-2023` #1 → `border-crisis-2023/i01` contested, shown as reverted: Rewrites the border-history paragraphs: adds that the Soviet Union unilaterally annulled the peace treaty by …
2. `civil-war-1918` #2 → `civil-war-1918/i06` contested, shown as reverted: Rewrites the lead and naming section: calls the Reds 'rebels', says the war 'turned out to be a war of libera…
3. `russification-independence` #2 → `russification-independence/i01` contested, shown as reverted: Adds passages saying the Jägers knowingly committed treason and 'took the path of a coup' against the lawful …
4. `winter-war` #2 → `winter-war/i04` contested, shown as reverted: Adds two paragraphs on Häyhä's kill count: a 1941 Hurtti Ukko interview (headline '600 kills for sure', 259 w…
5. `nato-debate` #5 → `nato-debate/i01` campaign, shown as reverted: Adds or puts back an Afghanistan passage in the list of Finland's wars saying Finland takes part in war opera…

**5 worst** (misses first, then false positives, overclaims and duplicates by rank)

1. **missed** `austerity-politics/i01` contested: Adds to Stubb's private-life section that his social-media use has annoyed citizens and that under his govern… (page 11396, `austerity-politics`)
2. **missed** `austerity-politics/i02` contested: Adds that Katainen's record as finance minister 2007-2011 caused the Finns Party's 2011 'jytky', voters signa… (page 10858, `austerity-politics`)
3. **missed** `austerity-politics/i12` contested: Adds a section 'Statement on nuclear power' saying that in Soininvaara's view nuclear power fights the greenh… (page 9059, `austerity-politics`)
4. **missed** `austerity-politics/i18` contested: Adds a 'Language questions' section saying Stubb did not arrange German interpretation at the start of the EU… (page 11396, `austerity-politics`)
5. **missed** `austerity-politics/i21` contested: Adds that during the 2007 Tehy nurses' strike Soininvaara wrote that if someone had to die because of the str… (page 9059, `austerity-politics`)

**Unlabelled:** 362 shown incident(s) have no golden item → `research/28-s1-revert/results/s1-reverts-v2.dev.unlabelled.jsonl` for the E3 judge.

**Failures:** none.

**Cost:** 1.9 s per request (max 4.7 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run signals --name s1-reverts-v2 -- python3 research/28-s1-revert/variant.py --class-version v2
```

</details>

