### Eval scorecard · `r2-c` · retrieval · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 11 topics, 43 requests · commit `f4bb08596ee6` · 2026-11-14 15:34:13 UTC · 0 failed

| Metric | Value | Δ vs `r1-vector` |
|---|---:|---:|
| recall@5 | 0.14 | -0.04 |
| recall@10 | 0.23 | -0.05 |
| recall@20 | 0.33 | -0.05 |
| ndcg@10 | 0.46 | -0.08 |
| oos_detection_rate | 0.00 | 0.00 |
| oos_false_alarm_rate | 0.00 | 0.00 |
| seconds_per_topic | 3.35 | +0.14 |
| llm_calls | 0 | 0 |
| input_tokens | 0 | 0 |
| output_tokens | 0 | 0 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 10 | 0.15 | 0.24 | 0.35 | 0.48 |
| fi | 10 | 0.24 | 0.38 | 0.51 | 0.73 |
| broad | 10 | 0.08 | 0.13 | 0.21 | 0.26 |
| sv | 10 | 0.10 | 0.17 | 0.27 | 0.37 |

**5 best queries**

1. `austerity-politics.fi`: ndcg@10 0.80, recall@10 0.38; missed Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2), Aktiivimalli (grade 2, rank 14), Matti Vanhanen (grade 1), Osmo Soininvaara (grade 1)
2. `russification-independence.fi`: ndcg@10 0.80, recall@10 0.29; missed Helmikuun manifesti (grade 2, rank 18), Jääkäripataljoona 27 (grade 2, rank 17), Suomen suuriruhtinaskunta (grade 1), Eugen Schauman (grade 1, rank 11), Suuri adressi (grade 1, rank 20)
3. `covid-19-finland.fi`: ndcg@10 0.76, recall@10 0.58; missed Etäopetus koronaviruspandemian aikana Suomessa (grade 2, rank 17), Koronavilkku (grade 2), Sanna Marin (grade 1), Kuntavaalit 2021 (grade 1), Marinin hallitus (grade 1, rank 12)
4. `ceded-karelia.fi`: ndcg@10 0.75, recall@10 0.40; missed Heimosodat (grade 2), ProKarelia (grade 2), Itä-Karjalan kysymys (grade 2), Viipuri (grade 1), Käkisalmi (grade 1)
5. `border-crisis-2023.fi`: ndcg@10 0.75, recall@10 0.46; missed Rajavartiolaitos (grade 1), Vartius (grade 1), Nuijamaan raja-asema (grade 1, rank 15), Raja-Jooseppi (ylikulkupaikka) (grade 1, rank 13), Imatran raja-asema (grade 1, rank 14)

**5 worst queries**

1. `russification-independence.sv`: recall@10 0.04, ndcg@10 0.07; missed Sortovuodet (grade 2, rank 18), Nikolai Bobrikov (grade 2), Suomen itsenäistyminen (grade 2, rank 20), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2, rank 14)
2. `russification-independence.broad`: recall@10 0.04, ndcg@10 0.07; missed Jääkäriliike (grade 2), Sortovuodet (grade 2, rank 18), Nikolai Bobrikov (grade 2), Suomen itsenäistyminen (grade 2, rank 11), Helmikuun manifesti (grade 2, rank 14)
3. `austerity-politics.broad`: recall@10 0.05, ndcg@10 0.24; missed Jyrki Katainen (grade 2), Juha Sipilä (grade 2), Kataisen hallitus (grade 2), Sipilän hallitus (grade 2), Suomi 2010-luvulla (grade 2)
4. `ceded-karelia.broad`: recall@10 0.05, ndcg@10 0.09; missed Heimosodat (grade 2), Luovutettu Karjala (grade 2), Karjala-kysymys (grade 2), ProKarelia (grade 2), Luovutetut alueet (grade 2)
5. `nato-debate.broad`: recall@10 0.06, ndcg@10 0.04; missed Suomen Nato-keskustelu (grade 2), Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2), Suomen Nato-jäsenyys (grade 2)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `civil-war-1918.en` | en | topic | 0.16 | 0.36 | no | 2.2 |
| `civil-war-1918.fi` | fi | topic | 0.37 | 0.73 | no | 2.0 |
| `civil-war-1918.broad` | broad | topic | 0.21 | 0.46 | no | 1.8 |
| `civil-war-1918.sv` | sv | topic | 0.21 | 0.43 | no | 2.0 |
| `continuation-war.en` | en | topic | 0.26 | 0.57 | no | 2.4 |
| `continuation-war.fi` | fi | topic | 0.32 | 0.71 | no | 2.1 |
| `continuation-war.broad` | broad | topic | 0.21 | 0.50 | no | 1.8 |
| `continuation-war.sv` | sv | topic | 0.16 | 0.47 | no | 2.0 |
| `winter-war.en` | en | topic | 0.36 | 0.70 | no | 2.1 |
| `winter-war.fi` | fi | topic | 0.36 | 0.70 | no | 14.7 |
| `winter-war.broad` | broad | topic | 0.21 | 0.42 | no | 6.6 |
| `winter-war.sv` | sv | topic | 0.29 | 0.63 | no | 2.6 |
| `ceded-karelia.en` | en | topic | 0.20 | 0.33 | no | 2.8 |
| `ceded-karelia.fi` | fi | topic | 0.40 | 0.75 | no | 2.2 |
| `ceded-karelia.broad` | broad | topic | 0.05 | 0.09 | no | 11.0 |
| `ceded-karelia.sv` | sv | topic | 0.20 | 0.32 | no | 2.7 |
| `nato-debate.en` | en | topic | 0.25 | 0.35 | no | 2.8 |
| `nato-debate.fi` | fi | topic | 0.38 | 0.71 | no | 2.3 |
| `nato-debate.broad` | broad | topic | 0.06 | 0.04 | no | 6.1 |
| `nato-debate.sv` | sv | topic | 0.06 | 0.06 | no | 2.0 |
| `finlandization.en` | en | topic | 0.14 | 0.47 | no | 8.9 |
| `finlandization.fi` | fi | topic | 0.23 | 0.59 | no | 1.9 |
| `finlandization.broad` | broad | topic | 0.09 | 0.11 | no | 3.4 |
| `finlandization.sv` | sv | topic | 0.18 | 0.57 | no | 3.4 |
| `austerity-politics.en` | en | topic | 0.24 | 0.65 | no | 2.0 |
| `austerity-politics.fi` | fi | topic | 0.38 | 0.80 | no | 1.8 |
| `austerity-politics.broad` | broad | topic | 0.05 | 0.24 | no | 1.8 |
| `austerity-politics.sv` | sv | topic | 0.19 | 0.47 | no | 9.6 |
| `covid-19-finland.en` | en | topic | 0.25 | 0.38 | no | 2.0 |
| `covid-19-finland.fi` | fi | topic | 0.58 | 0.76 | no | 2.0 |
| `covid-19-finland.broad` | broad | topic | 0.08 | 0.24 | no | 2.8 |
| `covid-19-finland.sv` | sv | topic | 0.17 | 0.32 | no | 4.5 |
| `russification-independence.en` | en | topic | 0.21 | 0.44 | no | 2.1 |
| `russification-independence.fi` | fi | topic | 0.29 | 0.80 | no | 2.1 |
| `russification-independence.broad` | broad | topic | 0.04 | 0.07 | no | 2.2 |
| `russification-independence.sv` | sv | topic | 0.04 | 0.07 | no | 2.0 |
| `border-crisis-2023.en` | en | control-quiet | 0.31 | 0.51 | no | 2.2 |
| `border-crisis-2023.fi` | fi | control-quiet | 0.46 | 0.75 | no | 1.8 |
| `border-crisis-2023.broad` | broad | control-quiet | 0.31 | 0.47 | no | 6.1 |
| `border-crisis-2023.sv` | sv | control-quiet | 0.23 | 0.34 | no | 2.4 |
| `french-revolution.en` | en | control-out-of-corpus | – | – | no | 2.4 |
| `french-revolution.fi` | fi | control-out-of-corpus | – | – | no | 1.3 |
| `french-revolution.broad` | broad | control-out-of-corpus | – | – | no | 1.5 |

</details>

**Failures:** none.

**Cost:** 3.4 s per request (max 14.7 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --name r2-c -- python3 research/23-r2-keyword/variant.py --mode C
```

</details>
