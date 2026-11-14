# P1.S9 Classification Scorecard (golden `2026-11-14.1`, dev split, 238 items)

| Prompt | Model | ok / N | Cat acc (all) | Incidents acc | Non-inc acc | Graffiti filt | Contested/camp wrongly filt | Verbatim ev | Calls/item | Mean ms/item | Tok/item | $/item |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `classify-edit-facts` | `gemini-4-carbon` | 238/238 | 67.7% | 68.1% | 67.0% | 27/29 (93.1%) | **0**/64 | 99.9% | 1.00 | 11808 | 3123 | $0.00913 |
| `classify-edit-facts` | `gemini-4-hydrogen` | 238/238 | 66.0% | 67.4% | 63.7% | 26/29 (89.7%) | **0**/64 | 99.9% | 1.00 | 6039 | 3736 | $0.01144 |
| `classify-edit-batch` | `gemini-4-carbon` | 238/238 | 63.8% | 63.2% | 64.8% | 27/29 (93.1%) | **0**/64 | 99.9% | 0.28 | 5499 | 1764 | $0.00623 |
| `classify-edit-batch` | `gemini-4-hydrogen` | 238/238 | 67.2% | 68.8% | 64.8% | 26/29 (89.7%) | **0**/64 | 99.8% | 0.28 | 3016 | 2010 | $0.00653 |

## Per-Topic Cost & Throughput (`dev` topics with items)

| Prompt | Model | Topics | Calls / topic | Tokens / topic | Latency ms / topic | Total $ |
|---|---|---:|---:|---:|---:|---:|
| `classify-edit-facts` | `gemini-4-carbon` | 10 | 23.8 | 74338 | 281030 | $2.1735 |
| `classify-edit-facts` | `gemini-4-hydrogen` | 10 | 23.8 | 88912 | 143722 | $2.7232 |
| `classify-edit-batch` | `gemini-4-carbon` | 10 | 6.6 | 41972 | 130886 | $1.4835 |
| `classify-edit-batch` | `gemini-4-hydrogen` | 10 | 6.6 | 47844 | 71790 | $1.5547 |

## `classify-edit-facts` · `gemini-4-carbon`
- **Outcomes:** ok=238
- **Category accuracy:** 159/235 (67.7% all, 67.7% on ok; coarse 3-way 90.2%; contested+campaign 79.7%)
- **Graffiti filter (`conf >= 4`):** filtered 46/238 total (19.3%); golden `graffiti` filtered 27/29 (93.1%); contested/campaign wrongly filtered: **0**
- **Evidence verbatim:** 745/746 exact (99.9%), 745/746 normalised (99.9%); all-spans-exact items: 99.6%

| Golden \ Pred | vand | test | gfe | disp | pov | false | src | other | non_ok | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vand` | **36** | 0 | 0 | 0 | 1 | 1 | 0 | 1 | 0 | 92.3% |
| `test` | 3 | **6** | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 60.0% |
| `gfe` | 0 | 1 | **35** | 7 | 1 | 1 | 0 | 2 | 0 | 74.5% |
| `disp` | 0 | 0 | 5 | **18** | 5 | 0 | 0 | 0 | 0 | 64.3% |
| `pov` | 0 | 0 | 1 | 2 | **37** | 0 | 0 | 0 | 0 | 92.5% |
| `false` | 0 | 0 | 2 | 0 | 1 | **5** | 0 | 0 | 0 | 62.5% |
| `src` | 0 | 0 | 0 | 0 | 0 | 0 | **1** | 0 | 0 | 100.0% |
| `other` | 1 | 0 | 24 | 11 | 4 | 0 | 1 | **21** | 0 | 33.9% |

## `classify-edit-facts` · `gemini-4-hydrogen`
- **Outcomes:** ok=238
- **Category accuracy:** 155/235 (66.0% all, 66.0% on ok; coarse 3-way 91.9%; contested+campaign 76.6%)
- **Graffiti filter (`conf >= 4`):** filtered 44/238 total (18.5%); golden `graffiti` filtered 26/29 (89.7%); contested/campaign wrongly filtered: **0**
- **Evidence verbatim:** 666/667 exact (99.9%), 666/667 normalised (99.9%); all-spans-exact items: 99.6%

| Golden \ Pred | vand | test | gfe | disp | pov | false | src | other | non_ok | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vand` | **35** | 0 | 0 | 0 | 1 | 1 | 0 | 2 | 0 | 89.7% |
| `test` | 3 | **6** | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 60.0% |
| `gfe` | 0 | 0 | **38** | 7 | 0 | 0 | 0 | 2 | 0 | 80.9% |
| `disp` | 0 | 0 | 7 | **20** | 0 | 1 | 0 | 0 | 0 | 71.4% |
| `pov` | 0 | 0 | 2 | 4 | **33** | 1 | 0 | 0 | 0 | 82.5% |
| `false` | 0 | 0 | 1 | 0 | 1 | **5** | 1 | 0 | 0 | 62.5% |
| `src` | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0.0% |
| `other` | 2 | 1 | 24 | 14 | 3 | 0 | 0 | **18** | 0 | 29.0% |

## `classify-edit-batch` · `gemini-4-carbon`
- **Outcomes:** ok=238
- **Category accuracy:** 150/235 (63.8% all, 63.8% on ok; coarse 3-way 86.0%; contested+campaign 70.3%)
- **Graffiti filter (`conf >= 4`):** filtered 48/238 total (20.2%); golden `graffiti` filtered 27/29 (93.1%); contested/campaign wrongly filtered: **0**
- **Evidence verbatim:** 686/687 exact (99.9%), 687/687 normalised (100.0%); all-spans-exact items: 99.6%

| Golden \ Pred | vand | test | gfe | disp | pov | false | src | other | non_ok | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vand` | **36** | 0 | 0 | 0 | 1 | 1 | 0 | 1 | 0 | 92.3% |
| `test` | 4 | **5** | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 50.0% |
| `gfe` | 0 | 1 | **33** | 8 | 1 | 2 | 2 | 0 | 0 | 70.2% |
| `disp` | 0 | 0 | 5 | **12** | 7 | 3 | 1 | 0 | 0 | 42.9% |
| `pov` | 0 | 0 | 1 | 2 | **36** | 0 | 1 | 0 | 0 | 90.0% |
| `false` | 0 | 0 | 1 | 0 | 1 | **5** | 1 | 0 | 0 | 62.5% |
| `src` | 0 | 0 | 0 | 0 | 0 | 0 | **1** | 0 | 0 | 100.0% |
| `other` | 3 | 1 | 21 | 11 | 3 | 0 | 1 | **22** | 0 | 35.5% |

## `classify-edit-batch` · `gemini-4-hydrogen`
- **Outcomes:** ok=238
- **Category accuracy:** 158/235 (67.2% all, 67.2% on ok; coarse 3-way 90.2%; contested+campaign 75.0%)
- **Graffiti filter (`conf >= 4`):** filtered 45/238 total (18.9%); golden `graffiti` filtered 26/29 (89.7%); contested/campaign wrongly filtered: **0**
- **Evidence verbatim:** 566/567 exact (99.8%), 566/567 normalised (99.8%); all-spans-exact items: 99.6%

| Golden \ Pred | vand | test | gfe | disp | pov | false | src | other | non_ok | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vand` | **36** | 0 | 0 | 0 | 1 | 0 | 0 | 2 | 0 | 92.3% |
| `test` | 4 | **4** | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 40.0% |
| `gfe` | 0 | 0 | **40** | 5 | 2 | 0 | 0 | 0 | 0 | 85.1% |
| `disp` | 0 | 0 | 8 | **16** | 4 | 0 | 0 | 0 | 0 | 57.1% |
| `pov` | 0 | 0 | 3 | 2 | **34** | 0 | 1 | 0 | 0 | 85.0% |
| `false` | 0 | 0 | 1 | 0 | 1 | **5** | 1 | 0 | 0 | 62.5% |
| `src` | 0 | 0 | 0 | 0 | 0 | 0 | **1** | 0 | 0 | 100.0% |
| `other` | 3 | 1 | 26 | 8 | 2 | 0 | 0 | **22** | 0 | 35.5% |
