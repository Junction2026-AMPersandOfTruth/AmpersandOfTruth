### Eval scorecard · `r3-b` · retrieval · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 11 topics, 43 requests · commit `f4bb08596ee6-dirty` · 2026-11-14 16:05:04 UTC · 0 failed

| Metric | Value | Δ vs `r1-vector` |
|---|---:|---:|
| recall@5 | 0.23 | +0.05 |
| recall@10 | 0.37 | +0.08 |
| recall@20 | 0.53 | +0.14 |
| ndcg@10 | 0.66 | +0.12 |
| oos_detection_rate | 0.00 | 0.00 |
| oos_false_alarm_rate | 0.00 | 0.00 |
| seconds_per_topic | 11.15 | +7.94 |
| llm_calls | 43 | +43 |
| input_tokens | 71,239 | +71,239 |
| output_tokens | 23,162 | +23,162 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 10 | 0.24 | 0.38 | 0.54 | 0.70 |
| fi | 10 | 0.25 | 0.38 | 0.56 | 0.71 |
| broad | 10 | 0.20 | 0.34 | 0.46 | 0.56 |
| sv | 10 | 0.23 | 0.37 | 0.54 | 0.66 |

**5 best queries**

1. `covid-19-finland.en`: ndcg@10 0.93, recall@10 0.75; missed Suomen koronavirusepidemiaa ennustavat mallit (grade 2, rank 11), Sanna Marin (grade 1, rank 15), Kuntavaalit 2021 (grade 1, rank 16)
2. `covid-19-finland.fi`: ndcg@10 0.91, recall@10 0.75; missed Koronaviruspandemia Ahvenanmaalla (grade 2, rank 11), Sanna Marin (grade 1), Kuntavaalit 2021 (grade 1, rank 15)
3. `border-crisis-2023.sv`: ndcg@10 0.90, recall@10 0.62; missed Vartius (grade 1), Nuijamaan raja-asema (grade 1, rank 18), Raja-Jooseppi (ylikulkupaikka) (grade 1), Suomen rajat (grade 1, rank 16), Kuusamon raja-asema (grade 1, rank 17)
4. `covid-19-finland.broad`: ndcg@10 0.87, recall@10 0.75; missed Suomen koronavirusepidemiaa ennustavat mallit (grade 2, rank 12), Koronavilkku (grade 2, rank 15), Kuntavaalit 2021 (grade 1, rank 18)
5. `border-crisis-2023.broad`: ndcg@10 0.86, recall@10 0.54; missed Vartius (grade 1), Nuijamaan raja-asema (grade 1), Raja-Jooseppi (ylikulkupaikka) (grade 1), Suomen rajat (grade 1, rank 17), Sallan raja-asema (grade 1, rank 11)

**5 worst queries**

1. `russification-independence.broad`: recall@10 0.12, ndcg@10 0.34; missed Jääkäriliike (grade 2), Sortovuodet (grade 2, rank 11), Nikolai Bobrikov (grade 2), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2)
2. `finlandization.broad`: recall@10 0.14, ndcg@10 0.30; missed Urho Kekkonen (grade 2), Suomettuminen (grade 2, rank 12), Paasikiven–Kekkosen linja (grade 2), Noottikriisi (grade 2), Yöpakkaset (grade 2)
3. `finlandization.sv`: recall@10 0.18, ndcg@10 0.60; missed YYA-sopimus (grade 2), Paasikiven–Kekkosen linja (grade 2, rank 16), Suomen ja Venäjän suhteet (grade 2), Kansakunta rähmällään (grade 2), Paavo Lipponen (grade 1)
4. `nato-debate.broad`: recall@10 0.19, ndcg@10 0.22; missed Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2), Suomen Nato-jäsenyys (grade 2, rank 20), Sauli Niinistö (grade 1)
5. `austerity-politics.broad`: recall@10 0.19, ndcg@10 0.30; missed Juha Sipilä (grade 2), Kataisen hallitus (grade 2, rank 19), Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2), Aktiivimalli (grade 2)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `civil-war-1918.en` | en | topic | 0.37 | 0.65 | no | 10.5 |
| `civil-war-1918.fi` | fi | topic | 0.42 | 0.80 | no | 10.7 |
| `civil-war-1918.broad` | broad | topic | 0.42 | 0.79 | no | 9.6 |
| `civil-war-1918.sv` | sv | topic | 0.37 | 0.77 | no | 10.1 |
| `continuation-war.en` | en | topic | 0.37 | 0.76 | no | 9.9 |
| `continuation-war.fi` | fi | topic | 0.32 | 0.73 | no | 9.6 |
| `continuation-war.broad` | broad | topic | 0.37 | 0.69 | no | 9.7 |
| `continuation-war.sv` | sv | topic | 0.32 | 0.73 | no | 9.0 |
| `winter-war.en` | en | topic | 0.36 | 0.61 | no | 8.2 |
| `winter-war.fi` | fi | topic | 0.43 | 0.77 | no | 9.3 |
| `winter-war.broad` | broad | topic | 0.43 | 0.61 | no | 11.0 |
| `winter-war.sv` | sv | topic | 0.43 | 0.75 | no | 9.6 |
| `ceded-karelia.en` | en | topic | 0.25 | 0.51 | no | 7.8 |
| `ceded-karelia.fi` | fi | topic | 0.25 | 0.51 | no | 9.8 |
| `ceded-karelia.broad` | broad | topic | 0.30 | 0.58 | no | 9.6 |
| `ceded-karelia.sv` | sv | topic | 0.30 | 0.59 | no | 8.6 |
| `nato-debate.en` | en | topic | 0.38 | 0.62 | no | 9.6 |
| `nato-debate.fi` | fi | topic | 0.38 | 0.69 | no | 8.3 |
| `nato-debate.broad` | broad | topic | 0.19 | 0.22 | no | 10.2 |
| `nato-debate.sv` | sv | topic | 0.31 | 0.37 | no | 8.0 |
| `finlandization.en` | en | topic | 0.27 | 0.70 | no | 7.8 |
| `finlandization.fi` | fi | topic | 0.23 | 0.55 | no | 9.7 |
| `finlandization.broad` | broad | topic | 0.14 | 0.30 | no | 9.2 |
| `finlandization.sv` | sv | topic | 0.18 | 0.60 | no | 10.1 |
| `austerity-politics.en` | en | topic | 0.24 | 0.64 | no | 9.6 |
| `austerity-politics.fi` | fi | topic | 0.24 | 0.51 | no | 8.8 |
| `austerity-politics.broad` | broad | topic | 0.19 | 0.30 | no | 14.9 |
| `austerity-politics.sv` | sv | topic | 0.29 | 0.58 | no | 13.1 |
| `covid-19-finland.en` | en | topic | 0.75 | 0.93 | no | 16.2 |
| `covid-19-finland.fi` | fi | topic | 0.75 | 0.91 | no | 13.8 |
| `covid-19-finland.broad` | broad | topic | 0.75 | 0.87 | no | 25.4 |
| `covid-19-finland.sv` | sv | topic | 0.67 | 0.83 | no | 11.6 |
| `russification-independence.en` | en | topic | 0.25 | 0.70 | no | 16.0 |
| `russification-independence.fi` | fi | topic | 0.29 | 0.77 | no | 15.3 |
| `russification-independence.broad` | broad | topic | 0.12 | 0.34 | no | 16.2 |
| `russification-independence.sv` | sv | topic | 0.21 | 0.53 | no | 13.5 |
| `border-crisis-2023.en` | en | control-quiet | 0.54 | 0.85 | no | 12.7 |
| `border-crisis-2023.fi` | fi | control-quiet | 0.46 | 0.81 | no | 11.2 |
| `border-crisis-2023.broad` | broad | control-quiet | 0.54 | 0.86 | no | 10.6 |
| `border-crisis-2023.sv` | sv | control-quiet | 0.62 | 0.90 | no | 8.9 |
| `french-revolution.en` | en | control-out-of-corpus | – | – | no | 10.9 |
| `french-revolution.fi` | fi | control-out-of-corpus | – | – | no | 11.4 |
| `french-revolution.broad` | broad | control-out-of-corpus | – | – | no | 13.3 |

</details>

**Failures:** none.

**Cost:** 11.2 s per request (max 25.4 s, 1 at a time), 43 LLM calls, 71,239 in / 23,162 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --name r3-b -- python3 research/24-r3-query/variant.py --mode B
```

</details>
