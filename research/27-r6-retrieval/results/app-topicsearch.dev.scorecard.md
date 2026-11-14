### Eval scorecard · `app-topicsearch` · retrieval · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 11 topics, 43 requests · commit `ef7717a6e07b` · 2026-11-14 21:36:32 UTC · 0 failed

| Metric | Value | Δ vs `r3-d` |
|---|---:|---:|
| recall@5 | 0.23 | -0.01 |
| recall@10 | 0.37 | 0.00 |
| recall@20 | 0.53 | 0.00 |
| ndcg@10 | 0.70 | +0.01 |
| oos_detection_rate | 1.00 | +1.00 |
| oos_false_alarm_rate | 0.00 | 0.00 |
| seconds_per_topic | 11.06 | +0.74 |
| llm_calls | 0 | -43 |
| input_tokens | 0 | -71,239 |
| output_tokens | 0 | -23,162 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 10 | 0.23 | 0.39 | 0.56 | 0.72 |
| fi | 10 | 0.23 | 0.39 | 0.58 | 0.75 |
| broad | 10 | 0.22 | 0.34 | 0.46 | 0.65 |
| sv | 10 | 0.22 | 0.37 | 0.54 | 0.68 |

**5 best queries**

1. `border-crisis-2023.sv`: ndcg@10 0.99, recall@10 0.77; missed Vartius (grade 1), Nuijamaan raja-asema (grade 1, rank 12), Kuusamon raja-asema (grade 1)
2. `border-crisis-2023.en`: ndcg@10 0.93, recall@10 0.69; missed Vartius (grade 1), Nuijamaan raja-asema (grade 1), Suomen rajat (grade 1, rank 14), Kuusamon raja-asema (grade 1)
3. `russification-independence.fi`: ndcg@10 0.92, recall@10 0.38; missed Toinen sortokausi (grade 2, rank 19), Suomen suuriruhtinaskunta (grade 1), Eugen Schauman (grade 1), Suuri adressi (grade 1), Franz Albert Seyn (grade 1)
4. `civil-war-1918.broad`: ndcg@10 0.89, recall@10 0.47; missed Suomen sisällissodan terrori (grade 2, rank 11), Carl Gustaf Emil Mannerheim (grade 1), Tampereen taistelu (grade 1), Suomen kansanvaltuuskunta (grade 1), Suomen sosialistinen työväentasavalta (grade 1, rank 20)
5. `border-crisis-2023.fi`: ndcg@10 0.85, recall@10 0.54; missed Vartius (grade 1), Nuijamaan raja-asema (grade 1, rank 13), Raja-Jooseppi (ylikulkupaikka) (grade 1, rank 17), Suomen rajat (grade 1), Kuusamon raja-asema (grade 1, rank 14)

**5 worst queries**

1. `russification-independence.broad`: recall@10 0.12, ndcg@10 0.35; missed Jääkäriliike (grade 2), Sortovuodet (grade 2), Nikolai Bobrikov (grade 2), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2)
2. `austerity-politics.broad`: recall@10 0.14, ndcg@10 0.45; missed Juha Sipilä (grade 2), Kataisen hallitus (grade 2), Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2), Aktiivimalli (grade 2)
3. `nato-debate.broad`: recall@10 0.19, ndcg@10 0.36; missed Suomen Nato-keskustelu (grade 2, rank 13), Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2), Sauli Niinistö (grade 1)
4. `finlandization.broad`: recall@10 0.23, ndcg@10 0.65; missed Urho Kekkonen (grade 2), Noottikriisi (grade 2), Yöpakkaset (grade 2), Kansakunta rähmällään (grade 2), Paavo Lipponen (grade 1)
5. `austerity-politics.sv`: recall@10 0.24, ndcg@10 0.51; missed Jyrki Katainen (grade 2, rank 18), Juha Sipilä (grade 2), Suomi 2010-luvulla (grade 2, rank 15), Kilpailukykysopimus (grade 2), Aktiivimalli (grade 2)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `civil-war-1918.en` | en | topic | 0.32 | 0.56 | no | 13.5 |
| `civil-war-1918.fi` | fi | topic | 0.32 | 0.65 | no | 12.7 |
| `civil-war-1918.broad` | broad | topic | 0.47 | 0.89 | no | 13.4 |
| `civil-war-1918.sv` | sv | topic | 0.32 | 0.60 | no | 13.3 |
| `continuation-war.en` | en | topic | 0.26 | 0.62 | no | 8.8 |
| `continuation-war.fi` | fi | topic | 0.32 | 0.63 | no | 8.9 |
| `continuation-war.broad` | broad | topic | 0.37 | 0.74 | no | 8.4 |
| `continuation-war.sv` | sv | topic | 0.32 | 0.70 | no | 10.1 |
| `winter-war.en` | en | topic | 0.43 | 0.76 | no | 10.7 |
| `winter-war.fi` | fi | topic | 0.43 | 0.77 | no | 10.5 |
| `winter-war.broad` | broad | topic | 0.36 | 0.66 | no | 11.3 |
| `winter-war.sv` | sv | topic | 0.36 | 0.72 | no | 10.8 |
| `ceded-karelia.en` | en | topic | 0.30 | 0.68 | no | 12.1 |
| `ceded-karelia.fi` | fi | topic | 0.30 | 0.67 | no | 12.7 |
| `ceded-karelia.broad` | broad | topic | 0.40 | 0.83 | no | 13.4 |
| `ceded-karelia.sv` | sv | topic | 0.30 | 0.69 | no | 10.3 |
| `nato-debate.en` | en | topic | 0.38 | 0.67 | no | 11.4 |
| `nato-debate.fi` | fi | topic | 0.38 | 0.74 | no | 12.3 |
| `nato-debate.broad` | broad | topic | 0.19 | 0.36 | no | 8.7 |
| `nato-debate.sv` | sv | topic | 0.31 | 0.65 | no | 11.9 |
| `finlandization.en` | en | topic | 0.27 | 0.75 | no | 8.7 |
| `finlandization.fi` | fi | topic | 0.32 | 0.75 | no | 10.8 |
| `finlandization.broad` | broad | topic | 0.23 | 0.65 | no | 13.9 |
| `finlandization.sv` | sv | topic | 0.27 | 0.77 | no | 13.8 |
| `austerity-politics.en` | en | topic | 0.29 | 0.73 | no | 11.7 |
| `austerity-politics.fi` | fi | topic | 0.29 | 0.71 | no | 12.1 |
| `austerity-politics.broad` | broad | topic | 0.14 | 0.45 | no | 8.5 |
| `austerity-politics.sv` | sv | topic | 0.24 | 0.51 | no | 12.2 |
| `covid-19-finland.en` | en | topic | 0.67 | 0.65 | no | 12.9 |
| `covid-19-finland.fi` | fi | topic | 0.67 | 0.79 | no | 12.5 |
| `covid-19-finland.broad` | broad | topic | 0.67 | 0.76 | no | 11.5 |
| `covid-19-finland.sv` | sv | topic | 0.58 | 0.54 | no | 11.7 |
| `russification-independence.en` | en | topic | 0.33 | 0.82 | no | 8.6 |
| `russification-independence.fi` | fi | topic | 0.38 | 0.92 | no | 9.3 |
| `russification-independence.broad` | broad | topic | 0.12 | 0.35 | no | 11.0 |
| `russification-independence.sv` | sv | topic | 0.25 | 0.66 | no | 9.7 |
| `border-crisis-2023.en` | en | control-quiet | 0.69 | 0.93 | no | 3.6 |
| `border-crisis-2023.fi` | fi | control-quiet | 0.54 | 0.85 | no | 11.7 |
| `border-crisis-2023.broad` | broad | control-quiet | 0.46 | 0.78 | no | 13.1 |
| `border-crisis-2023.sv` | sv | control-quiet | 0.77 | 0.99 | no | 11.9 |
| `french-revolution.en` | en | control-out-of-corpus | – | – | yes | 7.8 |
| `french-revolution.fi` | fi | control-out-of-corpus | – | – | yes | 11.5 |
| `french-revolution.broad` | broad | control-out-of-corpus | – | – | yes | 11.6 |

</details>

**Failures:** none.

**Cost:** 11.1 s per request (max 13.9 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --name app-topicsearch --timeout 300 -- scripts/investigate.sh variant
```

</details>
