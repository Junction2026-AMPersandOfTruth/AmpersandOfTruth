# P1.S9 Classification Scorecard (golden `2026-11-14.1`, dev split, 238 items)

| Prompt | Model | ok / N | Cat acc (all) | Incidents acc | Non-inc acc | Graffiti filt | Contested/camp wrongly filt | Verbatim ev | Calls/item | Mean ms/item | Tok/item | $/item |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `classify-edit` | `gemini-3.8-flash` | 238/238 | 66.0% | 70.1% | 59.3% | 29/29 (100.0%) | **0**/64 | 99.4% | 1.00 | 4488 | 2131 | $0.00239 |
| `classify-edit` | `gemini-4-carbon` | 238/238 | 66.8% | 67.4% | 65.9% | 28/29 (96.6%) | **0**/64 | 100.0% | 1.00 | 11753 | 2823 | $0.00860 |
| `classify-edit` | `gemini-4-hydrogen` | 238/238 | 67.7% | 69.4% | 64.8% | 27/29 (93.1%) | **0**/64 | 99.7% | 1.00 | 5604 | 3392 | $0.01072 |
| `classify-edit-two-stage` | `gemini-3.8-flash -> gemini-4-carbon` | 238/238 | 65.5% | 67.4% | 62.6% | 29/29 (100.0%) | **0**/64 | 100.0% | 1.78 | 13568 | 4398 | $0.00951 |

## Per-Topic Cost & Throughput (`dev` topics with items)

| Prompt | Model | Topics | Calls / topic | Tokens / topic | Latency ms / topic | Total $ |
|---|---|---:|---:|---:|---:|---:|
| `classify-edit` | `gemini-3.8-flash` | 10 | 23.8 | 50729 | 106806 | $0.5690 |
| `classify-edit` | `gemini-4-carbon` | 10 | 23.8 | 67190 | 279730 | $2.0476 |
| `classify-edit` | `gemini-4-hydrogen` | 10 | 23.8 | 80729 | 133369 | $2.5511 |
| `classify-edit-two-stage` | `gemini-3.8-flash -> gemini-4-carbon` | 10 | 42.4 | 104671 | 322923 | $2.2626 |

## `classify-edit` · `gemini-3.8-flash`
- **Outcomes:** ok=238
- **Category accuracy:** 155/235 (66.0% all, 66.0% on ok; coarse 3-way 92.3%; contested+campaign 78.1%)
- **Graffiti filter (`conf >= 4`):** filtered 52/238 total (21.8%); golden `graffiti` filtered 29/29 (100.0%); contested/campaign wrongly filtered: **0**
- **Evidence verbatim:** 614/618 exact (99.4%), 614/618 normalised (99.4%); all-spans-exact items: 98.3%

| Golden \ Pred | vand | test | gfe | disp | pov | false | src | other | non_ok | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vand` | **37** | 0 | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 94.9% |
| `test` | 4 | **5** | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 50.0% |
| `gfe` | 0 | 0 | **41** | 5 | 0 | 0 | 0 | 1 | 0 | 87.2% |
| `disp` | 0 | 0 | 8 | **19** | 1 | 0 | 0 | 0 | 0 | 67.9% |
| `pov` | 0 | 0 | 2 | 2 | **36** | 0 | 0 | 0 | 0 | 90.0% |
| `false` | 0 | 0 | 2 | 0 | 2 | **3** | 1 | 0 | 0 | 37.5% |
| `src` | 0 | 0 | 0 | 0 | 0 | 0 | **1** | 0 | 0 | 100.0% |
| `other` | 5 | 1 | 31 | 10 | 2 | 0 | 0 | **13** | 0 | 21.0% |

## `classify-edit` · `gemini-4-carbon`
- **Outcomes:** ok=238
- **Category accuracy:** 157/235 (66.8% all, 66.8% on ok; coarse 3-way 89.4%; contested+campaign 78.1%)
- **Graffiti filter (`conf >= 4`):** filtered 48/238 total (20.2%); golden `graffiti` filtered 28/29 (96.6%); contested/campaign wrongly filtered: **0**
- **Evidence verbatim:** 745/745 exact (100.0%), 745/745 normalised (100.0%); all-spans-exact items: 100.0%

| Golden \ Pred | vand | test | gfe | disp | pov | false | src | other | non_ok | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vand` | **36** | 0 | 0 | 0 | 1 | 1 | 0 | 1 | 0 | 92.3% |
| `test` | 4 | **6** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 60.0% |
| `gfe` | 0 | 1 | **36** | 8 | 0 | 1 | 0 | 1 | 0 | 76.6% |
| `disp` | 0 | 0 | 7 | **13** | 8 | 0 | 0 | 0 | 0 | 46.4% |
| `pov` | 0 | 0 | 0 | 2 | **38** | 0 | 0 | 0 | 0 | 95.0% |
| `false` | 0 | 0 | 1 | 0 | 1 | **6** | 0 | 0 | 0 | 75.0% |
| `src` | 0 | 0 | 0 | 0 | 0 | 0 | **1** | 0 | 0 | 100.0% |
| `other` | 3 | 0 | 23 | 9 | 5 | 0 | 1 | **21** | 0 | 33.9% |

## `classify-edit` · `gemini-4-hydrogen`
- **Outcomes:** ok=238
- **Category accuracy:** 159/235 (67.7% all, 67.7% on ok; coarse 3-way 91.5%; contested+campaign 75.0%)
- **Graffiti filter (`conf >= 4`):** filtered 46/238 total (19.3%); golden `graffiti` filtered 27/29 (93.1%); contested/campaign wrongly filtered: **0**
- **Evidence verbatim:** 644/646 exact (99.7%), 645/646 normalised (99.8%); all-spans-exact items: 99.2%

| Golden \ Pred | vand | test | gfe | disp | pov | false | src | other | non_ok | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vand` | **35** | 0 | 0 | 0 | 1 | 1 | 0 | 2 | 0 | 89.7% |
| `test` | 3 | **6** | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 60.0% |
| `gfe` | 0 | 0 | **41** | 5 | 0 | 0 | 0 | 1 | 0 | 87.2% |
| `disp` | 0 | 0 | 7 | **18** | 3 | 0 | 0 | 0 | 0 | 64.3% |
| `pov` | 0 | 0 | 2 | 3 | **34** | 0 | 1 | 0 | 0 | 85.0% |
| `false` | 0 | 0 | 1 | 0 | 1 | **5** | 1 | 0 | 0 | 62.5% |
| `src` | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0.0% |
| `other` | 2 | 1 | 23 | 13 | 3 | 0 | 0 | **20** | 0 | 32.3% |

## `classify-edit-two-stage` · `gemini-3.8-flash -> gemini-4-carbon`
- **Outcomes:** ok=238
- **Category accuracy:** 154/235 (65.5% all, 65.5% on ok; coarse 3-way 87.7%; contested+campaign 78.1%)
- **Graffiti filter (`conf >= 4`):** filtered 54/238 total (22.7%); golden `graffiti` filtered 29/29 (100.0%); contested/campaign wrongly filtered: **0**
- **Evidence verbatim:** 719/719 exact (100.0%), 719/719 normalised (100.0%); all-spans-exact items: 100.0%
- **Variant stage stats:** stage1_filtered=52, stage2_routed=186

| Golden \ Pred | vand | test | gfe | disp | pov | false | src | other | non_ok | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vand` | **37** | 0 | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 94.9% |
| `test` | 4 | **6** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 60.0% |
| `gfe` | 0 | 1 | **36** | 8 | 0 | 1 | 0 | 1 | 0 | 76.6% |
| `disp` | 0 | 0 | 7 | **13** | 8 | 0 | 0 | 0 | 0 | 46.4% |
| `pov` | 0 | 0 | 0 | 2 | **38** | 0 | 0 | 0 | 0 | 95.0% |
| `false` | 0 | 0 | 1 | 0 | 1 | **6** | 0 | 0 | 0 | 75.0% |
| `src` | 0 | 0 | 0 | 0 | 0 | 0 | **1** | 0 | 0 | 100.0% |
| `other` | 7 | 1 | 22 | 9 | 5 | 0 | 1 | **17** | 0 | 27.4% |
