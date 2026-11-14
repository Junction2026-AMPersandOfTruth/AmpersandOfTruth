# P1.S9 Classification Scorecard (golden `2026-11-14.1`, dev split, 238 items)

| Prompt | Model | ok / N | Cat acc (all) | Incidents acc | Non-inc acc | Graffiti filt | Contested/camp wrongly filt | Verbatim ev | Calls/item | Mean ms/item | Tok/item | $/item |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `classify-edit+refute` | `gemini-4-carbon + gemini-4-carbon` | 238/238 | 66.8% | 66.7% | 67.0% | 28/29 (96.6%) | **0**/64 | 100.0% | 1.26 | 14478 | 3575 | $0.01072 |

## Per-Topic Cost & Throughput (`dev` topics with items)

| Prompt | Model | Topics | Calls / topic | Tokens / topic | Latency ms / topic | Total $ |
|---|---|---:|---:|---:|---:|---:|
| `classify-edit+refute` | `gemini-4-carbon + gemini-4-carbon` | 10 | 30.1 | 85077 | 344570 | $2.5505 |

## `classify-edit+refute` · `gemini-4-carbon + gemini-4-carbon`
- **Outcomes:** ok=238
- **Category accuracy:** 157/235 (66.8% all, 66.8% on ok; coarse 3-way 91.5%; contested+campaign 76.6%)
- **Graffiti filter (`conf >= 4`):** filtered 48/238 total (20.2%); golden `graffiti` filtered 28/29 (96.6%); contested/campaign wrongly filtered: **0**
- **Evidence verbatim:** 740/740 exact (100.0%), 740/740 normalised (100.0%); all-spans-exact items: 100.0%
- **Variant stage stats:** refute_checked=63, refute_upheld=46, refute_downgraded=17, refute_non_ok=0

| Golden \ Pred | vand | test | gfe | disp | pov | false | src | other | non_ok | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vand` | **36** | 0 | 0 | 0 | 1 | 1 | 0 | 1 | 0 | 92.3% |
| `test` | 4 | **6** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 60.0% |
| `gfe` | 0 | 1 | **37** | 8 | 0 | 0 | 0 | 1 | 0 | 78.7% |
| `disp` | 0 | 0 | 8 | **18** | 2 | 0 | 0 | 0 | 0 | 64.3% |
| `pov` | 0 | 0 | 4 | 2 | **34** | 0 | 0 | 0 | 0 | 85.0% |
| `false` | 0 | 0 | 3 | 0 | 1 | **4** | 0 | 0 | 0 | 50.0% |
| `src` | 0 | 0 | 0 | 0 | 0 | 0 | **1** | 0 | 0 | 100.0% |
| `other` | 3 | 0 | 23 | 13 | 2 | 0 | 0 | **21** | 0 | 33.9% |
