# P1.S9b Gemini Decisions (`gemini-decision-1`) Scorecard (golden `2026-11-14.1`, dev split, 238 items)

## 1. Side-by-Side Comparison: Decisions API vs Generative S9 (`A.out.jsonl`)

| Run | Model | ok / N | Blocked | Cat acc (235) | Graffiti rule / cutoff | Graffiti rem (29) | Not-inc rem (94) | Contested/camp lost (64) | p50 / p95 ms | Tok/item | $/item |
|---|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|
| **Decisions A** | `gemini-decision-1` | 237/238 | 0.4% | 58.7% | `nonsense >= 0.642` | 27/29 (93.1%) | 47/94 (50.0%) | **0**/64 | 598 / 1279 | 1925 | $0.000192 |
| **Decisions C** | `gemini-decision-1` | 237/238 | 0.4% | 58.7% | `nonsense >= 0.601` | 26/29 (89.7%) | 43/94 (45.7%) | **0**/64 | 569 / 713 | 2105 | $0.000211 |
| S9-A (gemini-3.8-flash) | `gemini-3.8-flash` | 238/238 | 0.0% | 66.0% | `vand/test, conf>=4` | 29/29 (100.0%) | 50/94 (53.2%) | **0**/64 | 4210 / 7000 | 2131 | $0.00239 |
| S9-A (gemini-4-hydrogen) | `gemini-4-hydrogen` | 238/238 | 0.0% | 67.7% | `vand/test, conf>=4` | 27/29 (93.1%) | 44/94 (46.8%) | **0**/64 | 5568 / 8250 | 3392 | $0.01072 |
| S9-A (gemini-4-carbon) | `gemini-4-carbon` | 238/238 | 0.0% | 66.8% | `vand/test, conf>=4` | 28/29 (96.6%) | 45/94 (47.9%) | **0**/64 | 9786 / 27100 | 2823 | $0.00860 |

## 2. Graffiti Filter Detail (`nonsense` Probability)

| Variant | Max `nonsense` on contested/campaign | Highest max-graff safe `t` | Aggressive min-safe `t` | Graffiti rem @ `t` | All not-inc rem @ `t` (aggressive) | Reverted rem @ `t` (aggressive) |
|---|---:|---:|---:|---:|---:|---:|
| Decisions A | 0.4118 (`continuation-war/i06`) | `0.6416` | `0.4243` | 27/29 (93.1%) | 47/94 (50.0%) · agg 50/94 (53.2%) | 2/80 · agg 7/80 |
| Decisions C | 0.5826 (`continuation-war/i06`) | `0.6007` | `0.5833` | 26/29 (89.7%) | 43/94 (45.7%) · agg 45/94 (47.9%) | 4/80 · agg 4/80 |

## 3. Probability Calibration & Discrimination (`level` Score Distribution)

| Variant | Task | N | Base rate | ROC AUC | Brier | ECE (5-bin) |
|---|---|---:|---:|---:|---:|---:|
| Decisions A | Is an incident (`label != not-incident`, `P(level >= 1)`) | 237 | 60.8% | **0.926** | **0.101** | 0.023 |
| Decisions A | `>= contested` (`label in {contested, campaign}`, `P(level >= 2)`) | 237 | 27.0% | **0.954** | **0.096** | 0.125 |
| Decisions C | Is an incident (`label != not-incident`, `P(level >= 1)`) | 237 | 60.8% | **0.925** | **0.104** | 0.054 |
| Decisions C | `>= contested` (`label in {contested, campaign}`, `P(level >= 2)`) | 237 | 27.0% | **0.952** | **0.133** | 0.182 |

### 5-Bucket Reliability Tables — Decisions Variant A

| Bucket | `P(level >= 1)` n | Mean pred | Actual `is_incident` | `P(level >= 2)` n | Mean pred | Actual `>= contested` |
|---|---:|---:|---:|---:|---:|---:|
| `[0.0, 0.2)` | 58 | 0.069 | 3.4% | 92 | 0.064 | 0.0% |
| `[0.2, 0.4)` | 20 | 0.304 | 30.0% | 48 | 0.277 | 8.3% |
| `[0.4, 0.6)` | 19 | 0.502 | 63.2% | 28 | 0.495 | 21.4% |
| `[0.6, 0.8)` | 32 | 0.710 | 71.9% | 35 | 0.694 | 60.0% |
| `[0.8, 1.0]` | 108 | 0.930 | 93.5% | 34 | 0.872 | 97.1% |

### 5-Bucket Reliability Tables — Decisions Variant C

| Bucket | `P(level >= 1)` n | Mean pred | Actual `is_incident` | `P(level >= 2)` n | Mean pred | Actual `>= contested` |
|---|---:|---:|---:|---:|---:|---:|
| `[0.0, 0.2)` | 50 | 0.073 | 2.0% | 72 | 0.062 | 0.0% |
| `[0.2, 0.4)` | 15 | 0.296 | 20.0% | 44 | 0.297 | 2.3% |
| `[0.4, 0.6)` | 12 | 0.482 | 41.7% | 27 | 0.500 | 11.1% |
| `[0.6, 0.8)` | 34 | 0.710 | 52.9% | 42 | 0.700 | 33.3% |
| `[0.8, 1.0]` | 126 | 0.942 | 92.9% | 52 | 0.898 | 88.5% |

## 4. Judge Agreement (`argmax(level)` vs Golden Label)

| Variant | ok / N | Exact agreement (ok) | Exact agreement (all) | Within ±1 level | Overclaim rate | Underclaim rate |
|---|---:|---:|---:|---:|---:|---:|
| Decisions A | 237/238 | **72.2%** (171/237) | 71.8% | 95.8% | 16.5% (39) | 11.4% (27) |
| Decisions C | 237/238 | **66.2%** (157/237) | 66.0% | 94.1% | 28.3% (67) | 5.5% (13) |

### 4×4 Level Confusion — Decisions Variant A (rows = Golden, cols = `argmax(level)`)

| Golden \ Pred | `not-incident` (0) | `reverted` (1) | `contested` (2) | `campaign` (3) | `non_ok` | Row total |
|---|---:|---:|---:|---:|---:|---:|
| `not-incident` | **77** | 9 | 7 | 0 | 1 | 94 |
| `reverted` | 21 | **36** | 23 | 0 | 0 | 80 |
| `contested` | 3 | 2 | **58** | 0 | 0 | 63 |
| `campaign` | 0 | 0 | 1 | 0 | 0 | 1 |

### 4×4 Level Confusion — Decisions Variant C (rows = Golden, cols = `argmax(level)`)

| Golden \ Pred | `not-incident` (0) | `reverted` (1) | `contested` (2) | `campaign` (3) | `non_ok` | Row total |
|---|---:|---:|---:|---:|---:|---:|
| `not-incident` | **68** | 11 | 14 | 0 | 1 | 94 |
| `reverted` | 10 | **28** | 42 | 0 | 0 | 80 |
| `contested` | 0 | 2 | **61** | 0 | 0 | 63 |
| `campaign` | 0 | 0 | 1 | 0 | 0 | 1 |

## 5. Category Confusion — Decisions Variant A
- **Exact accuracy:** 138/235 (58.7% all, 59.0% on ok; incidents 59.0%; contested+campaign 65.6%; non-incidents 58.2%; `none_of_these` chosen 0 times).

| Golden \ Pred | vand | test | gfe | disp | pov | false | src | other | none | non_ok | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vand` | **31** | 5 | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 1 | 79.5% |
| `test` | 0 | **10** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 100.0% |
| `gfe` | 1 | 5 | **32** | 3 | 0 | 3 | 1 | 2 | 0 | 0 | 68.1% |
| `disp` | 0 | 1 | 5 | **11** | 10 | 0 | 0 | 1 | 0 | 0 | 39.3% |
| `pov` | 0 | 0 | 1 | 3 | **33** | 0 | 3 | 0 | 0 | 0 | 82.5% |
| `false` | 0 | 0 | 1 | 1 | 2 | **3** | 1 | 0 | 0 | 0 | 37.5% |
| `src` | 0 | 0 | 0 | 0 | 0 | 0 | **1** | 0 | 0 | 0 | 100.0% |
| `other` | 8 | 5 | 22 | 8 | 2 | 0 | 0 | **17** | 0 | 0 | 27.4% |

## 5. Category Confusion — Decisions Variant C
- **Exact accuracy:** 138/235 (58.7% all, 59.0% on ok; incidents 57.6%; contested+campaign 64.1%; non-incidents 60.4%; `none_of_these` chosen 0 times).

| Golden \ Pred | vand | test | gfe | disp | pov | false | src | other | none | non_ok | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vand` | **30** | 6 | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 1 | 76.9% |
| `test` | 0 | **9** | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 90.0% |
| `gfe` | 0 | 4 | **32** | 5 | 0 | 2 | 1 | 3 | 0 | 0 | 68.1% |
| `disp` | 1 | 0 | 3 | **13** | 9 | 1 | 0 | 1 | 0 | 0 | 46.4% |
| `pov` | 0 | 0 | 1 | 3 | **32** | 0 | 4 | 0 | 0 | 0 | 80.0% |
| `false` | 1 | 0 | 1 | 1 | 2 | **2** | 1 | 0 | 0 | 0 | 25.0% |
| `src` | 0 | 0 | 0 | 0 | 0 | 0 | **1** | 0 | 0 | 0 | 100.0% |
| `other` | 7 | 5 | 18 | 11 | 2 | 0 | 0 | **19** | 0 | 0 | 30.6% |

## 6. Auxiliary Yes/No (`noul`) Question Discrimination

| Variant | Question | Mean (`not-inc`) | Mean (`reverted`) | Mean (`contested`) | Mean (`campaign`) | AUC (`is_incident`) | AUC (`>= contested`) | AUC (`is_graffiti`) |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| A | `nonsense` | 0.536 | 0.125 | 0.063 | 0.060 | 0.199 | 0.265 | 0.962 |
| A | `factual_claim` | 0.207 | 0.609 | 0.703 | 0.843 | 0.869 | 0.752 | 0.145 |
| A | `changes_number_date_name` | 0.459 | 0.707 | 0.672 | 0.930 | 0.697 | 0.589 | 0.383 |
| A | `political_or_national_pov` | 0.141 | 0.121 | 0.527 | 0.509 | 0.725 | 0.876 | 0.367 |
| A | `contradicts_replaced_text` | 0.155 | 0.118 | 0.237 | 0.366 | 0.592 | 0.724 | 0.600 |
| A | `alters_citation` | 0.235 | 0.475 | 0.485 | 0.948 | 0.699 | 0.615 | 0.129 |
| A | `removal_disputes_accuracy` | 0.139 | 0.351 | 0.711 | 0.941 | 0.827 | 0.850 | 0.207 |
| C | `nonsense` | 0.503 | 0.115 | 0.055 | 0.035 | 0.201 | 0.245 | 0.955 |
| C | `factual_claim` | 0.207 | 0.614 | 0.692 | 0.837 | 0.869 | 0.749 | 0.126 |
| C | `changes_number_date_name` | 0.459 | 0.713 | 0.682 | 0.941 | 0.717 | 0.588 | 0.396 |
| C | `political_or_national_pov` | 0.146 | 0.136 | 0.521 | 0.601 | 0.739 | 0.884 | 0.372 |
| C | `contradicts_replaced_text` | 0.166 | 0.124 | 0.264 | 0.350 | 0.582 | 0.732 | 0.566 |
| C | `alters_citation` | 0.215 | 0.450 | 0.468 | 0.905 | 0.719 | 0.631 | 0.104 |
| C | `removal_disputes_accuracy` | 0.153 | 0.386 | 0.730 | 0.939 | 0.832 | 0.870 | 0.201 |
