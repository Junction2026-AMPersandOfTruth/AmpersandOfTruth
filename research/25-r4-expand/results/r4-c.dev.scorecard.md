### Eval scorecard · `r4-c` · retrieval · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 11 topics, 43 requests · commit `f4bb08596ee6` · 2026-11-14 15:50:08 UTC · 0 failed

| Metric | Value | Δ vs `r1-vector` |
|---|---:|---:|
| recall@5 | 0.16 | -0.02 |
| recall@10 | 0.23 | -0.05 |
| recall@20 | 0.36 | -0.03 |
| ndcg@10 | 0.45 | -0.09 |
| oos_detection_rate | 0.00 | 0.00 |
| oos_false_alarm_rate | 0.00 | 0.00 |
| seconds_per_topic | 0.55 | -2.66 |
| llm_calls | 0 | 0 |
| input_tokens | 0 | 0 |
| output_tokens | 0 | 0 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 10 | 0.19 | 0.27 | 0.41 | 0.53 |
| fi | 10 | 0.19 | 0.27 | 0.42 | 0.53 |
| broad | 10 | 0.12 | 0.18 | 0.28 | 0.33 |
| sv | 10 | 0.15 | 0.21 | 0.32 | 0.42 |

**5 best queries**

1. `austerity-politics.en`: ndcg@10 0.73, recall@10 0.29; missed Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2), Aktiivimalli (grade 2), Matti Vanhanen (grade 1), Osmo Soininvaara (grade 1)
2. `austerity-politics.fi`: ndcg@10 0.73, recall@10 0.29; missed Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2), Aktiivimalli (grade 2), Matti Vanhanen (grade 1), Osmo Soininvaara (grade 1)
3. `finlandization.fi`: ndcg@10 0.71, recall@10 0.32; missed YYA-sopimus (grade 2), Suomen ja Venäjän suhteet (grade 2), Kansakunta rähmällään (grade 2, rank 19), Paavo Lipponen (grade 1), Tarja Halonen (grade 1)
4. `ceded-karelia.en`: ndcg@10 0.67, recall@10 0.35; missed Heimosodat (grade 2), Luovutettu Karjala (grade 2), Luovutetut alueet (grade 2), Itä-Karjalan kysymys (grade 2), Viipuri (grade 1)
5. `border-crisis-2023.fi`: ndcg@10 0.66, recall@10 0.38; missed Suomen ja Venäjän rajakriisi 2023 (grade 2, rank 20), Rajavartiolaitos (grade 1), Vartius (grade 1), Nuijamaan raja-asema (grade 1), Raja-Jooseppi (ylikulkupaikka) (grade 1)

**5 worst queries**

1. `ceded-karelia.broad`: recall@10 0.05, ndcg@10 0.07; missed Heimosodat (grade 2), Luovutettu Karjala (grade 2), Karjala-kysymys (grade 2), ProKarelia (grade 2), Luovutetut alueet (grade 2)
2. `nato-debate.sv`: recall@10 0.06, ndcg@10 0.04; missed Suomen Nato-keskustelu (grade 2), Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2), Suomen Nato-jäsenyys (grade 2)
3. `russification-independence.sv`: recall@10 0.08, ndcg@10 0.20; missed Sortovuodet (grade 2), Nikolai Bobrikov (grade 2), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2), Jääkäripataljoona 27 (grade 2)
4. `russification-independence.broad`: recall@10 0.08, ndcg@10 0.28; missed Jääkäriliike (grade 2), Sortovuodet (grade 2, rank 19), Nikolai Bobrikov (grade 2), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2)
5. `nato-debate.broad`: recall@10 0.12, ndcg@10 0.10; missed Suomen Nato-keskustelu (grade 2), Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2), Suomen Nato-jäsenyys (grade 2)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `civil-war-1918.en` | en | topic | 0.26 | 0.60 | no | 0.5 |
| `civil-war-1918.fi` | fi | topic | 0.21 | 0.43 | no | 0.4 |
| `civil-war-1918.broad` | broad | topic | 0.21 | 0.45 | no | 0.5 |
| `civil-war-1918.sv` | sv | topic | 0.32 | 0.52 | no | 0.5 |
| `continuation-war.en` | en | topic | 0.32 | 0.60 | no | 0.6 |
| `continuation-war.fi` | fi | topic | 0.26 | 0.59 | no | 0.4 |
| `continuation-war.broad` | broad | topic | 0.32 | 0.51 | no | 0.5 |
| `continuation-war.sv` | sv | topic | 0.26 | 0.53 | no | 0.5 |
| `winter-war.en` | en | topic | 0.21 | 0.46 | no | 0.6 |
| `winter-war.fi` | fi | topic | 0.21 | 0.50 | no | 0.5 |
| `winter-war.broad` | broad | topic | 0.29 | 0.52 | no | 0.5 |
| `winter-war.sv` | sv | topic | 0.14 | 0.30 | no | 0.5 |
| `ceded-karelia.en` | en | topic | 0.35 | 0.67 | no | 0.5 |
| `ceded-karelia.fi` | fi | topic | 0.25 | 0.43 | no | 0.7 |
| `ceded-karelia.broad` | broad | topic | 0.05 | 0.07 | no | 0.5 |
| `ceded-karelia.sv` | sv | topic | 0.30 | 0.63 | no | 0.5 |
| `nato-debate.en` | en | topic | 0.19 | 0.14 | no | 0.5 |
| `nato-debate.fi` | fi | topic | 0.25 | 0.20 | no | 0.5 |
| `nato-debate.broad` | broad | topic | 0.12 | 0.10 | no | 0.6 |
| `nato-debate.sv` | sv | topic | 0.06 | 0.04 | no | 0.5 |
| `finlandization.en` | en | topic | 0.23 | 0.58 | no | 0.5 |
| `finlandization.fi` | fi | topic | 0.32 | 0.71 | no | 0.6 |
| `finlandization.broad` | broad | topic | 0.14 | 0.22 | no | 0.6 |
| `finlandization.sv` | sv | topic | 0.14 | 0.46 | no | 0.6 |
| `austerity-politics.en` | en | topic | 0.29 | 0.73 | no | 0.6 |
| `austerity-politics.fi` | fi | topic | 0.29 | 0.73 | no | 0.6 |
| `austerity-politics.broad` | broad | topic | 0.14 | 0.23 | no | 0.6 |
| `austerity-politics.sv` | sv | topic | 0.19 | 0.47 | no | 0.5 |
| `covid-19-finland.en` | en | topic | 0.33 | 0.49 | no | 0.5 |
| `covid-19-finland.fi` | fi | topic | 0.33 | 0.52 | no | 0.5 |
| `covid-19-finland.broad` | broad | topic | 0.17 | 0.28 | no | 0.7 |
| `covid-19-finland.sv` | sv | topic | 0.33 | 0.46 | no | 0.6 |
| `russification-independence.en` | en | topic | 0.17 | 0.53 | no | 0.6 |
| `russification-independence.fi` | fi | topic | 0.17 | 0.58 | no | 0.5 |
| `russification-independence.broad` | broad | topic | 0.08 | 0.28 | no | 0.4 |
| `russification-independence.sv` | sv | topic | 0.08 | 0.20 | no | 0.5 |
| `border-crisis-2023.en` | en | control-quiet | 0.31 | 0.54 | no | 0.6 |
| `border-crisis-2023.fi` | fi | control-quiet | 0.38 | 0.66 | no | 0.6 |
| `border-crisis-2023.broad` | broad | control-quiet | 0.31 | 0.62 | no | 0.6 |
| `border-crisis-2023.sv` | sv | control-quiet | 0.31 | 0.55 | no | 0.5 |
| `french-revolution.en` | en | control-out-of-corpus | – | – | no | 0.5 |
| `french-revolution.fi` | fi | control-out-of-corpus | – | – | no | 0.9 |
| `french-revolution.broad` | broad | control-out-of-corpus | – | – | no | 1.1 |

</details>

**Failures:** none.

**Cost:** 0.6 s per request (max 1.1 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --name r4-c -- python3 research/25-r4-expand/variant.py --mode C
```

</details>
