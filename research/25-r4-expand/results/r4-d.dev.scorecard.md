### Eval scorecard · `r4-d` · retrieval · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 11 topics, 43 requests · commit `f4bb08596ee6` · 2026-11-14 15:50:32 UTC · 0 failed

| Metric | Value | Δ vs `r1-vector` |
|---|---:|---:|
| recall@5 | 0.13 | -0.06 |
| recall@10 | 0.21 | -0.07 |
| recall@20 | 0.36 | -0.02 |
| ndcg@10 | 0.41 | -0.12 |
| oos_detection_rate | 0.00 | 0.00 |
| oos_false_alarm_rate | 0.00 | 0.00 |
| seconds_per_topic | 1.09 | -2.12 |
| llm_calls | 0 | 0 |
| input_tokens | 0 | 0 |
| output_tokens | 0 | 0 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 10 | 0.13 | 0.23 | 0.38 | 0.45 |
| fi | 10 | 0.18 | 0.28 | 0.47 | 0.56 |
| broad | 10 | 0.09 | 0.16 | 0.26 | 0.26 |
| sv | 10 | 0.11 | 0.20 | 0.34 | 0.39 |

**5 best queries**

1. `austerity-politics.fi`: ndcg@10 0.76, recall@10 0.33; missed Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2), Aktiivimalli (grade 2), Matti Vanhanen (grade 1), Osmo Soininvaara (grade 1)
2. `austerity-politics.en`: ndcg@10 0.71, recall@10 0.29; missed Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2), Aktiivimalli (grade 2), Matti Vanhanen (grade 1), Osmo Soininvaara (grade 1)
3. `civil-war-1918.fi`: ndcg@10 0.63, recall@10 0.26; missed Valkoiset (grade 2), Suomen sisällissodan terrori (grade 2), Carl Gustaf Emil Mannerheim (grade 1), Tampereen taistelu (grade 1), Suomen kansanvaltuuskunta (grade 1)
4. `winter-war.en`: ndcg@10 0.63, recall@10 0.36; missed Moskovan rauhansopimus (1940) (grade 2), Talvisodan tausta (grade 2), Raatteen tien taistelu (grade 1), Mannerheim-linja (grade 1), Kollaan taistelu (grade 1, rank 11)
5. `ceded-karelia.fi`: ndcg@10 0.61, recall@10 0.30; missed Heimosodat (grade 2), ProKarelia (grade 2), Itä-Karjalan kysymys (grade 2), Viipuri (grade 1), Käkisalmi (grade 1)

**5 worst queries**

1. `border-crisis-2023.broad`: recall@10 0.08, ndcg@10 0.15; missed Suomen ja Venäjän rajakriisi 2023 (grade 2), Suomen ja Venäjän rajan esteaita (grade 2, rank 15), Rajavartiolaitos (grade 1, rank 16), Vartius (grade 1), Nuijamaan raja-asema (grade 1)
2. `border-crisis-2023.en`: recall@10 0.08, ndcg@10 0.19; missed Suomen ja Venäjän rajakriisi 2023 (grade 2, rank 16), Suomen ja Venäjän rajan esteaita (grade 2, rank 12), Rajavartiolaitos (grade 1, rank 13), Vartius (grade 1), Nuijamaan raja-asema (grade 1)
3. `border-crisis-2023.sv`: recall@10 0.08, ndcg@10 0.19; missed Suomen ja Venäjän rajakriisi 2023 (grade 2, rank 14), Suomen ja Venäjän rajan esteaita (grade 2, rank 12), Rajavartiolaitos (grade 1, rank 11), Vartius (grade 1), Nuijamaan raja-asema (grade 1)
4. `russification-independence.broad`: recall@10 0.08, ndcg@10 0.18; missed Jääkäriliike (grade 2), Sortovuodet (grade 2, rank 13), Nikolai Bobrikov (grade 2), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2)
5. `russification-independence.sv`: recall@10 0.08, ndcg@10 0.32; missed Sortovuodet (grade 2, rank 14), Nikolai Bobrikov (grade 2), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2, rank 20), Jääkäripataljoona 27 (grade 2)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `civil-war-1918.en` | en | topic | 0.21 | 0.45 | no | 3.7 |
| `civil-war-1918.fi` | fi | topic | 0.26 | 0.63 | no | 1.1 |
| `civil-war-1918.broad` | broad | topic | 0.26 | 0.44 | no | 0.7 |
| `civil-war-1918.sv` | sv | topic | 0.26 | 0.56 | no | 1.1 |
| `continuation-war.en` | en | topic | 0.26 | 0.48 | no | 0.7 |
| `continuation-war.fi` | fi | topic | 0.16 | 0.40 | no | 1.1 |
| `continuation-war.broad` | broad | topic | 0.32 | 0.56 | no | 0.8 |
| `continuation-war.sv` | sv | topic | 0.16 | 0.36 | no | 1.0 |
| `winter-war.en` | en | topic | 0.36 | 0.63 | no | 0.9 |
| `winter-war.fi` | fi | topic | 0.21 | 0.54 | no | 1.0 |
| `winter-war.broad` | broad | topic | 0.14 | 0.33 | no | 0.8 |
| `winter-war.sv` | sv | topic | 0.29 | 0.54 | no | 1.1 |
| `ceded-karelia.en` | en | topic | 0.25 | 0.52 | no | 1.0 |
| `ceded-karelia.fi` | fi | topic | 0.30 | 0.61 | no | 1.7 |
| `ceded-karelia.broad` | broad | topic | 0.10 | 0.24 | no | 1.2 |
| `ceded-karelia.sv` | sv | topic | 0.20 | 0.44 | no | 0.7 |
| `nato-debate.en` | en | topic | 0.19 | 0.18 | no | 1.0 |
| `nato-debate.fi` | fi | topic | 0.25 | 0.44 | no | 1.0 |
| `nato-debate.broad` | broad | topic | 0.12 | 0.11 | no | 0.8 |
| `nato-debate.sv` | sv | topic | 0.19 | 0.17 | no | 0.8 |
| `finlandization.en` | en | topic | 0.18 | 0.51 | no | 0.9 |
| `finlandization.fi` | fi | topic | 0.14 | 0.46 | no | 1.8 |
| `finlandization.broad` | broad | topic | 0.14 | 0.20 | no | 0.8 |
| `finlandization.sv` | sv | topic | 0.23 | 0.59 | no | 1.1 |
| `austerity-politics.en` | en | topic | 0.29 | 0.71 | no | 0.6 |
| `austerity-politics.fi` | fi | topic | 0.33 | 0.76 | no | 1.8 |
| `austerity-politics.broad` | broad | topic | 0.14 | 0.28 | no | 3.3 |
| `austerity-politics.sv` | sv | topic | 0.24 | 0.40 | no | 1.0 |
| `covid-19-finland.en` | en | topic | 0.25 | 0.35 | no | 0.9 |
| `covid-19-finland.fi` | fi | topic | 0.50 | 0.56 | no | 0.8 |
| `covid-19-finland.broad` | broad | topic | 0.17 | 0.13 | no | 0.8 |
| `covid-19-finland.sv` | sv | topic | 0.25 | 0.33 | no | 1.0 |
| `russification-independence.en` | en | topic | 0.21 | 0.47 | no | 0.7 |
| `russification-independence.fi` | fi | topic | 0.25 | 0.60 | no | 1.1 |
| `russification-independence.broad` | broad | topic | 0.08 | 0.18 | no | 1.1 |
| `russification-independence.sv` | sv | topic | 0.08 | 0.32 | no | 0.8 |
| `border-crisis-2023.en` | en | control-quiet | 0.08 | 0.19 | no | 0.8 |
| `border-crisis-2023.fi` | fi | control-quiet | 0.38 | 0.60 | no | 0.8 |
| `border-crisis-2023.broad` | broad | control-quiet | 0.08 | 0.15 | no | 0.8 |
| `border-crisis-2023.sv` | sv | control-quiet | 0.08 | 0.19 | no | 0.8 |
| `french-revolution.en` | en | control-out-of-corpus | – | – | no | 1.0 |
| `french-revolution.fi` | fi | control-out-of-corpus | – | – | no | 0.9 |
| `french-revolution.broad` | broad | control-out-of-corpus | – | – | no | 0.9 |

</details>

**Failures:** none.

**Cost:** 1.1 s per request (max 3.7 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --name r4-d -- python3 research/25-r4-expand/variant.py --mode D --w 0.05
```

</details>
