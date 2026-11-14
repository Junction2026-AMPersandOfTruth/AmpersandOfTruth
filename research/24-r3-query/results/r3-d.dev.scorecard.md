### Eval scorecard · `r3-d` · retrieval · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 11 topics, 43 requests · commit `f4bb08596ee6-dirty` · 2026-11-14 16:17:19 UTC · 0 failed

| Metric | Value | Δ vs `r1-vector` |
|---|---:|---:|
| recall@5 | 0.23 | +0.05 |
| recall@10 | 0.37 | +0.09 |
| recall@20 | 0.53 | +0.14 |
| ndcg@10 | 0.69 | +0.15 |
| oos_detection_rate | 0.00 | 0.00 |
| oos_false_alarm_rate | 0.00 | 0.00 |
| seconds_per_topic | 10.32 | +7.11 |
| llm_calls | 43 | +43 |
| input_tokens | 71,239 | +71,239 |
| output_tokens | 23,162 | +23,162 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 10 | 0.23 | 0.38 | 0.53 | 0.72 |
| fi | 10 | 0.25 | 0.40 | 0.56 | 0.75 |
| broad | 10 | 0.23 | 0.34 | 0.46 | 0.61 |
| sv | 10 | 0.23 | 0.37 | 0.55 | 0.69 |

**5 best queries**

1. `border-crisis-2023.sv`: ndcg@10 0.88, recall@10 0.62; missed Vartius (grade 1), Nuijamaan raja-asema (grade 1, rank 18), Raja-Jooseppi (ylikulkupaikka) (grade 1), Suomen rajat (grade 1, rank 16), Kuusamon raja-asema (grade 1, rank 17)
2. `russification-independence.en`: ndcg@10 0.86, recall@10 0.33; missed Ensimmäinen sortokausi (grade 2), Toinen sortokausi (grade 2), Suomen suuriruhtinaskunta (grade 1, rank 14), Eugen Schauman (grade 1), Suuri adressi (grade 1)
3. `russification-independence.fi`: ndcg@10 0.85, recall@10 0.33; missed Ensimmäinen sortokausi (grade 2, rank 11), Toinen sortokausi (grade 2, rank 13), Suomen suuriruhtinaskunta (grade 1), Eugen Schauman (grade 1), Suuri adressi (grade 1)
4. `covid-19-finland.en`: ndcg@10 0.85, recall@10 0.75; missed Suomen koronavirusepidemiaa ennustavat mallit (grade 2, rank 12), Sanna Marin (grade 1, rank 16), Kuntavaalit 2021 (grade 1, rank 17)
5. `civil-war-1918.broad`: ndcg@10 0.85, recall@10 0.42; missed Suomen sisällissodan terrori (grade 2, rank 14), Carl Gustaf Emil Mannerheim (grade 1), Tampereen taistelu (grade 1), Suomen kansanvaltuuskunta (grade 1), Suomen sotasurmat (grade 1, rank 11)

**5 worst queries**

1. `russification-independence.broad`: recall@10 0.12, ndcg@10 0.34; missed Jääkäriliike (grade 2), Sortovuodet (grade 2, rank 13), Nikolai Bobrikov (grade 2), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2)
2. `austerity-politics.broad`: recall@10 0.14, ndcg@10 0.26; missed Jyrki Katainen (grade 2, rank 12), Juha Sipilä (grade 2), Kataisen hallitus (grade 2), Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2)
3. `nato-debate.broad`: recall@10 0.19, ndcg@10 0.37; missed Suomen Nato-keskustelu (grade 2, rank 11), Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2), Sauli Niinistö (grade 1)
4. `russification-independence.sv`: recall@10 0.21, ndcg@10 0.63; missed Nikolai Bobrikov (grade 2), Sortokausien aktivismi (grade 2, rank 11), Jääkäripataljoona 27 (grade 2, rank 17), Ensimmäinen sortokausi (grade 2), Toinen sortokausi (grade 2, rank 20)
5. `finlandization.broad`: recall@10 0.23, ndcg@10 0.65; missed Urho Kekkonen (grade 2), Noottikriisi (grade 2), Yöpakkaset (grade 2), Kansakunta rähmällään (grade 2), Paavo Lipponen (grade 1)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `civil-war-1918.en` | en | topic | 0.37 | 0.68 | no | 21.8 |
| `civil-war-1918.fi` | fi | topic | 0.42 | 0.72 | no | 10.0 |
| `civil-war-1918.broad` | broad | topic | 0.42 | 0.85 | no | 13.3 |
| `civil-war-1918.sv` | sv | topic | 0.37 | 0.64 | no | 9.9 |
| `continuation-war.en` | en | topic | 0.26 | 0.61 | no | 13.7 |
| `continuation-war.fi` | fi | topic | 0.26 | 0.61 | no | 15.8 |
| `continuation-war.broad` | broad | topic | 0.32 | 0.57 | no | 11.3 |
| `continuation-war.sv` | sv | topic | 0.32 | 0.70 | no | 11.6 |
| `winter-war.en` | en | topic | 0.29 | 0.58 | no | 11.0 |
| `winter-war.fi` | fi | topic | 0.43 | 0.77 | no | 9.8 |
| `winter-war.broad` | broad | topic | 0.43 | 0.71 | no | 11.6 |
| `winter-war.sv` | sv | topic | 0.36 | 0.72 | no | 9.8 |
| `ceded-karelia.en` | en | topic | 0.30 | 0.65 | no | 7.5 |
| `ceded-karelia.fi` | fi | topic | 0.35 | 0.71 | no | 9.6 |
| `ceded-karelia.broad` | broad | topic | 0.35 | 0.75 | no | 9.6 |
| `ceded-karelia.sv` | sv | topic | 0.35 | 0.72 | no | 12.3 |
| `nato-debate.en` | en | topic | 0.38 | 0.67 | no | 9.8 |
| `nato-debate.fi` | fi | topic | 0.44 | 0.82 | no | 9.5 |
| `nato-debate.broad` | broad | topic | 0.19 | 0.37 | no | 9.3 |
| `nato-debate.sv` | sv | topic | 0.31 | 0.54 | no | 8.8 |
| `finlandization.en` | en | topic | 0.27 | 0.76 | no | 12.1 |
| `finlandization.fi` | fi | topic | 0.32 | 0.77 | no | 14.7 |
| `finlandization.broad` | broad | topic | 0.23 | 0.65 | no | 8.6 |
| `finlandization.sv` | sv | topic | 0.23 | 0.69 | no | 9.0 |
| `austerity-politics.en` | en | topic | 0.29 | 0.72 | no | 7.5 |
| `austerity-politics.fi` | fi | topic | 0.29 | 0.72 | no | 8.4 |
| `austerity-politics.broad` | broad | topic | 0.14 | 0.26 | no | 7.8 |
| `austerity-politics.sv` | sv | topic | 0.24 | 0.61 | no | 8.1 |
| `covid-19-finland.en` | en | topic | 0.75 | 0.85 | no | 9.3 |
| `covid-19-finland.fi` | fi | topic | 0.67 | 0.75 | no | 10.3 |
| `covid-19-finland.broad` | broad | topic | 0.67 | 0.73 | no | 9.2 |
| `covid-19-finland.sv` | sv | topic | 0.67 | 0.76 | no | 8.8 |
| `russification-independence.en` | en | topic | 0.33 | 0.86 | no | 8.2 |
| `russification-independence.fi` | fi | topic | 0.33 | 0.85 | no | 7.3 |
| `russification-independence.broad` | broad | topic | 0.12 | 0.34 | no | 8.6 |
| `russification-independence.sv` | sv | topic | 0.21 | 0.63 | no | 8.8 |
| `border-crisis-2023.en` | en | control-quiet | 0.54 | 0.81 | no | 9.7 |
| `border-crisis-2023.fi` | fi | control-quiet | 0.46 | 0.78 | no | 9.0 |
| `border-crisis-2023.broad` | broad | control-quiet | 0.54 | 0.83 | no | 9.6 |
| `border-crisis-2023.sv` | sv | control-quiet | 0.62 | 0.88 | no | 8.8 |
| `french-revolution.en` | en | control-out-of-corpus | – | – | no | 10.0 |
| `french-revolution.fi` | fi | control-out-of-corpus | – | – | no | 12.8 |
| `french-revolution.broad` | broad | control-out-of-corpus | – | – | no | 11.3 |

</details>

**Failures:** none.

**Cost:** 10.3 s per request (max 21.8 s, 1 at a time), 43 LLM calls, 71,239 in / 23,162 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --name r3-d -- python3 research/24-r3-query/variant.py --mode D
```

</details>
