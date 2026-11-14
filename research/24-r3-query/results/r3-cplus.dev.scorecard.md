### Eval scorecard · `r3-cplus` · retrieval · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 11 topics, 43 requests · commit `f4bb08596ee6-dirty` · 2026-11-14 16:13:04 UTC · 0 failed

| Metric | Value | Δ vs `r1-vector` |
|---|---:|---:|
| recall@5 | 0.22 | +0.03 |
| recall@10 | 0.33 | +0.05 |
| recall@20 | 0.49 | +0.10 |
| ndcg@10 | 0.63 | +0.09 |
| oos_detection_rate | 0.00 | 0.00 |
| oos_false_alarm_rate | 0.00 | 0.00 |
| seconds_per_topic | 5.92 | +2.71 |
| llm_calls | 43 | +43 |
| input_tokens | 71,239 | +71,239 |
| output_tokens | 23,162 | +23,162 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 10 | 0.24 | 0.35 | 0.49 | 0.69 |
| fi | 10 | 0.26 | 0.41 | 0.55 | 0.76 |
| broad | 10 | 0.15 | 0.22 | 0.41 | 0.42 |
| sv | 10 | 0.22 | 0.34 | 0.51 | 0.67 |

**5 best queries**

1. `border-crisis-2023.fi`: ndcg@10 0.94, recall@10 0.69; missed Rajavartiolaitos (grade 1), Vartius (grade 1, rank 12), Raja-Jooseppi (ylikulkupaikka) (grade 1, rank 17), Suomen rajat (grade 1, rank 18)
2. `border-crisis-2023.sv`: ndcg@10 0.90, recall@10 0.62; missed Rajavartiolaitos (grade 1, rank 13), Vartius (grade 1), Nuijamaan raja-asema (grade 1), Imatran raja-asema (grade 1, rank 19), Kuusamon raja-asema (grade 1, rank 20)
3. `covid-19-finland.fi`: ndcg@10 0.87, recall@10 0.67; missed Koronavilkku (grade 2, rank 17), Sanna Marin (grade 1, rank 11), Kuntavaalit 2021 (grade 1), Marinin hallitus (grade 1, rank 12)
4. `border-crisis-2023.en`: ndcg@10 0.86, recall@10 0.54; missed Rajavartiolaitos (grade 1), Vartius (grade 1), Nuijamaan raja-asema (grade 1), Imatran raja-asema (grade 1, rank 17), Sallan raja-asema (grade 1, rank 14)
5. `nato-debate.fi`: ndcg@10 0.81, recall@10 0.44; missed Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Luettelo Suomen historian sodista (grade 1), Suomettuminen (grade 1, rank 20), Erkki Tuomioja (grade 1), Alexander Stubb (grade 1, rank 19)

**5 worst queries**

1. `austerity-politics.broad`: recall@10 0.05, ndcg@10 0.12; missed Jyrki Katainen (grade 2, rank 12), Juha Sipilä (grade 2), Kataisen hallitus (grade 2), Sipilän hallitus (grade 2, rank 13), Suomi 2010-luvulla (grade 2)
2. `nato-debate.broad`: recall@10 0.06, ndcg@10 0.08; missed Suomen Nato-keskustelu (grade 2), Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2, rank 20), Suomen Nato-jäsenyys (grade 2, rank 18)
3. `finlandization.broad`: recall@10 0.09, ndcg@10 0.31; missed Urho Kekkonen (grade 2), Suomettuminen (grade 2), Paasikiven–Kekkosen linja (grade 2, rank 17), Noottikriisi (grade 2), Yöpakkaset (grade 2)
4. `ceded-karelia.broad`: recall@10 0.10, ndcg@10 0.40; missed Heimosodat (grade 2, rank 12), Luovutettu Karjala (grade 2), ProKarelia (grade 2), Luovutetut alueet (grade 2), Itä-Karjalan kysymys (grade 2, rank 17)
5. `russification-independence.broad`: recall@10 0.12, ndcg@10 0.29; missed Jääkäriliike (grade 2), Sortovuodet (grade 2, rank 15), Nikolai Bobrikov (grade 2), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `civil-war-1918.en` | en | topic | 0.26 | 0.64 | no | 4.9 |
| `civil-war-1918.fi` | fi | topic | 0.26 | 0.62 | no | 4.8 |
| `civil-war-1918.broad` | broad | topic | 0.37 | 0.64 | no | 8.3 |
| `civil-war-1918.sv` | sv | topic | 0.26 | 0.61 | no | 5.1 |
| `continuation-war.en` | en | topic | 0.32 | 0.69 | no | 5.0 |
| `continuation-war.fi` | fi | topic | 0.32 | 0.76 | no | 5.6 |
| `continuation-war.broad` | broad | topic | 0.37 | 0.68 | no | 5.1 |
| `continuation-war.sv` | sv | topic | 0.26 | 0.66 | no | 5.3 |
| `winter-war.en` | en | topic | 0.50 | 0.81 | no | 5.1 |
| `winter-war.fi` | fi | topic | 0.43 | 0.79 | no | 4.9 |
| `winter-war.broad` | broad | topic | 0.43 | 0.62 | no | 5.2 |
| `winter-war.sv` | sv | topic | 0.43 | 0.79 | no | 4.9 |
| `ceded-karelia.en` | en | topic | 0.40 | 0.69 | no | 8.4 |
| `ceded-karelia.fi` | fi | topic | 0.40 | 0.67 | no | 5.4 |
| `ceded-karelia.broad` | broad | topic | 0.10 | 0.40 | no | 6.8 |
| `ceded-karelia.sv` | sv | topic | 0.30 | 0.54 | no | 5.8 |
| `nato-debate.en` | en | topic | 0.25 | 0.38 | no | 7.7 |
| `nato-debate.fi` | fi | topic | 0.44 | 0.81 | no | 5.2 |
| `nato-debate.broad` | broad | topic | 0.06 | 0.08 | no | 7.8 |
| `nato-debate.sv` | sv | topic | 0.31 | 0.50 | no | 5.0 |
| `finlandization.en` | en | topic | 0.27 | 0.70 | no | 5.0 |
| `finlandization.fi` | fi | topic | 0.27 | 0.62 | no | 6.0 |
| `finlandization.broad` | broad | topic | 0.09 | 0.31 | no | 6.5 |
| `finlandization.sv` | sv | topic | 0.27 | 0.73 | no | 4.9 |
| `austerity-politics.en` | en | topic | 0.29 | 0.72 | no | 5.3 |
| `austerity-politics.fi` | fi | topic | 0.33 | 0.76 | no | 6.5 |
| `austerity-politics.broad` | broad | topic | 0.05 | 0.12 | no | 8.5 |
| `austerity-politics.sv` | sv | topic | 0.24 | 0.58 | no | 6.6 |
| `covid-19-finland.en` | en | topic | 0.42 | 0.69 | no | 4.8 |
| `covid-19-finland.fi` | fi | topic | 0.67 | 0.87 | no | 6.3 |
| `covid-19-finland.broad` | broad | topic | 0.25 | 0.32 | no | 4.8 |
| `covid-19-finland.sv` | sv | topic | 0.50 | 0.72 | no | 7.0 |
| `russification-independence.en` | en | topic | 0.29 | 0.69 | no | 12.2 |
| `russification-independence.fi` | fi | topic | 0.29 | 0.77 | no | 5.0 |
| `russification-independence.broad` | broad | topic | 0.12 | 0.29 | no | 4.7 |
| `russification-independence.sv` | sv | topic | 0.25 | 0.65 | no | 4.9 |
| `border-crisis-2023.en` | en | control-quiet | 0.54 | 0.86 | no | 4.9 |
| `border-crisis-2023.fi` | fi | control-quiet | 0.69 | 0.94 | no | 6.4 |
| `border-crisis-2023.broad` | broad | control-quiet | 0.38 | 0.74 | no | 5.0 |
| `border-crisis-2023.sv` | sv | control-quiet | 0.62 | 0.90 | no | 5.2 |
| `french-revolution.en` | en | control-out-of-corpus | – | – | no | 8.1 |
| `french-revolution.fi` | fi | control-out-of-corpus | – | – | no | 4.8 |
| `french-revolution.broad` | broad | control-out-of-corpus | – | – | no | 5.0 |

</details>

**Failures:** none.

**Cost:** 5.9 s per request (max 12.2 s, 1 at a time), 43 LLM calls, 71,239 in / 23,162 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --name r3-cplus -- python3 research/24-r3-query/variant.py --mode C+
```

</details>
