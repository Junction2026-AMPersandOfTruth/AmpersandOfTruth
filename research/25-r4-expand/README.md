# P1.R4 (#25) · Expand retrieval through categories, cross-language siblings and revert activity

`variant.py --mode A|B|C|D` are contract §2.1 retrieval variants; all four pass `research/eval/validate.py variant retrieval`.
The code was written by a Gemini worker, which timed out twice on Gemini API network errors. The orchestrator (Claude)
validated the modes, re-ran the probes and measurements, and wrote this README.

| Mode | What it does |
|---|---|
| **A** category neighbours | R1 top `--seed-k` (10) → each seed's most specific category (2 … `--max-cat-size` members, skipping `Suomen historia` and maintenance categories) → members ranked by cosine to the query → at most `--cap` (20) new pages appended |
| **B** category-scoped search | 3 best-matching categories from R1's top-5 fi hits → recursive CTE down `category_link` (depth ≤ 3, aborts if over 5 s) → all members ranked by cosine |
| **C** cross-language fold | `--fold rrf` (default; RRF of the fi-only rank and the folded en-only rank), `all` (= R1), `fi`, `en` |
| **D** contested first | R1 top 30 re-ranked by `distance − w · log1p(reverts)`, with `reverts` = `research.s1_page_stats.signal_total` (S1 #28; non-excluded, non-bot, non-self reverts) |

## Probe check (eyeball only, not a score: 14 in-corpus probes with 37 expected fi titles, plus 2 out-of-corpus)

`results/probe.<mode>.md`. Latency and "new pages" were measured over all 16 probes **while S4, S6 and the golden-slice export
were loading the shared VPS**, so the absolute seconds are inflated; compare them relative to each other.

| Variant | Expected in top 10 | Expected in top 20 | Mean pages not in R1's top 20 | Median s | Max s |
|---|---:|---:|---:|---:|---:|
| R1 (`research/8-r1-vector`) | 20 | 22 | — | ~0.8 | — |
| A category neighbours | 20 | 23 | 7.8 | 9.6 | 29.9 |
| B category-scoped | 19 | 25 | 11.5 | 6.1 | 33.0 |
| C fold = rrf | 20 | 25 | 7.6 | 1.1 | 4.7 |
| D w = 0.01 | 21 | **31** | 3.2 | 2.5 | 23.0 |
| D w = 0.02 (default) | 22 | **31** | 4.1 | 2.5 | 21.9 |
| **D w = 0.05** | **24** | **31** | 4.7 | 1.3 | 5.7 |

- The **category walk (B) is cheap in SQL**: the recursive CTE took about 0.1 s for 94–362 candidates. B's latency is the
  per-candidate distance computation plus the mariadb CLI round trips over the tunnel, not the CTE. Its choice of categories
  can drift: "Talvisodan syyt" also picked *Vuoden 1989 suomalaiset elokuvat* (the 1989 Winter War film).
- **A and B flood the result:** 8–12 of the 20 results are new pages that R1 did not have, for little or no gain in the top 10.
  That is the risk the issue warned about, and it costs downstream signal mining.
- **C (RRF fold) ≈ R1:** cross-lingual matching is already good, so folding languages differently changes little.
- **D is the winner.** Re-ranking R1's own top 30 by revert activity adds no new articles beyond R1's top 30, gives the best top-20
  of the bake-off so far (31/37, the same as R3-D/E), and its top 10 improves as `w` grows. It is also the cheapest mode once
  S1 is precomputed. The caveat is circular: it favours articles with many reverts, and our probe expectations are
  contested demo articles. On the golden set, check that `control-quiet` topics do not lose their real articles.

## Recommendation

Score **D (w = 0.05)** on the golden set, and **combine it with R3-E**: R3 decides *which* articles are about the topic,
D decides *where there is something to find*. Drop A and B unless the golden set shows recall gains on topics whose sub-articles
R1 misses entirely. Treat C as a no-op.
