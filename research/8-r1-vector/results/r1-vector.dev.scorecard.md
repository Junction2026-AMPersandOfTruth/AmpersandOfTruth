### Eval scorecard · `r1-vector` · retrieval · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 11 topics, 43 requests · commit `ebfdadc8bb6f-dirty` · 2026-11-14 14:49:45 UTC · 0 failed

| Metric | Value |
|---|---:|
| recall@5 | 0.19 |
| recall@10 | 0.28 |
| recall@20 | 0.39 |
| ndcg@10 | 0.54 |
| oos_detection_rate | 0.00 |
| oos_false_alarm_rate | 0.00 |
| seconds_per_topic | 3.21 |
| llm_calls | 0 |
| input_tokens | 0 |
| output_tokens | 0 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 10 | 0.19 | 0.28 | 0.41 | 0.56 |
| fi | 10 | 0.26 | 0.40 | 0.52 | 0.74 |
| broad | 10 | 0.13 | 0.19 | 0.27 | 0.34 |
| sv | 10 | 0.17 | 0.25 | 0.36 | 0.51 |

**5 best queries**

1. `border-crisis-2023.fi`: ndcg@10 0.92, recall@10 0.69; missed Rajavartiolaitos (grade 1), Vartius (grade 1, rank 12), Raja-Jooseppi (ylikulkupaikka) (grade 1, rank 11), Suomen rajat (grade 1, rank 17)
2. `russification-independence.fi`: ndcg@10 0.89, recall@10 0.33; missed Helmikuun manifesti (grade 2), Suomen suuriruhtinaskunta (grade 1), Eugen Schauman (grade 1), Suuri adressi (grade 1), Franz Albert Seyn (grade 1)
3. `nato-debate.fi`: ndcg@10 0.81, recall@10 0.44; missed Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Luettelo Suomen historian sodista (grade 1), Suomettuminen (grade 1), Erkki Tuomioja (grade 1), Alexander Stubb (grade 1, rank 19)
4. `border-crisis-2023.sv`: ndcg@10 0.81, recall@10 0.46; missed Rajavartiolaitos (grade 1, rank 18), Vartius (grade 1), Nuijamaan raja-asema (grade 1), Raja-Jooseppi (ylikulkupaikka) (grade 1, rank 12), Imatran raja-asema (grade 1)
5. `austerity-politics.fi`: ndcg@10 0.80, recall@10 0.38; missed Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2), Aktiivimalli (grade 2), Matti Vanhanen (grade 1), Osmo Soininvaara (grade 1)

**5 worst queries**

1. `austerity-politics.broad`: recall@10 0.05, ndcg@10 0.24; missed Jyrki Katainen (grade 2), Juha Sipilä (grade 2), Kataisen hallitus (grade 2), Sipilän hallitus (grade 2), Suomi 2010-luvulla (grade 2)
2. `ceded-karelia.broad`: recall@10 0.05, ndcg@10 0.15; missed Heimosodat (grade 2), Luovutettu Karjala (grade 2), Karjala-kysymys (grade 2, rank 11), ProKarelia (grade 2), Luovutetut alueet (grade 2)
3. `covid-19-finland.broad`: recall@10 0.08, ndcg@10 0.09; missed Etäopetus koronaviruspandemian aikana Suomessa (grade 2), Suomen koronaviruspandemian aikajana (grade 2), Suomen koronavirusepidemiaa ennustavat mallit (grade 2), Suomen toimenpiteet koronaviruspandemiaa vastaan (grade 2), Suomen koronavirusstrategia (grade 2)
4. `finlandization.broad`: recall@10 0.09, ndcg@10 0.20; missed Urho Kekkonen (grade 2), Suomettuminen (grade 2), YYA-sopimus (grade 2, rank 16), Paasikiven–Kekkosen linja (grade 2), Noottikriisi (grade 2)
5. `nato-debate.broad`: recall@10 0.12, ndcg@10 0.11; missed Suomen Nato-keskustelu (grade 2), Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2), Suomen Nato-jäsenyys (grade 2)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `civil-war-1918.en` | en | topic | 0.26 | 0.52 | no | 24.5 |
| `civil-war-1918.fi` | fi | topic | 0.26 | 0.52 | no | 20.6 |
| `civil-war-1918.broad` | broad | topic | 0.26 | 0.46 | no | 2.4 |
| `civil-war-1918.sv` | sv | topic | 0.21 | 0.35 | no | 5.6 |
| `continuation-war.en` | en | topic | 0.26 | 0.56 | no | 2.4 |
| `continuation-war.fi` | fi | topic | 0.32 | 0.71 | no | 6.7 |
| `continuation-war.broad` | broad | topic | 0.32 | 0.58 | no | 2.6 |
| `continuation-war.sv` | sv | topic | 0.26 | 0.61 | no | 2.3 |
| `winter-war.en` | en | topic | 0.36 | 0.66 | no | 2.6 |
| `winter-war.fi` | fi | topic | 0.29 | 0.65 | no | 2.5 |
| `winter-war.broad` | broad | topic | 0.36 | 0.51 | no | 1.5 |
| `winter-war.sv` | sv | topic | 0.21 | 0.54 | no | 1.5 |
| `ceded-karelia.en` | en | topic | 0.35 | 0.62 | no | 1.7 |
| `ceded-karelia.fi` | fi | topic | 0.35 | 0.70 | no | 1.5 |
| `ceded-karelia.broad` | broad | topic | 0.05 | 0.15 | no | 1.5 |
| `ceded-karelia.sv` | sv | topic | 0.30 | 0.58 | no | 1.9 |
| `nato-debate.en` | en | topic | 0.12 | 0.22 | no | 1.5 |
| `nato-debate.fi` | fi | topic | 0.44 | 0.81 | no | 1.6 |
| `nato-debate.broad` | broad | topic | 0.12 | 0.11 | no | 1.6 |
| `nato-debate.sv` | sv | topic | 0.19 | 0.27 | no | 1.5 |
| `finlandization.en` | en | topic | 0.18 | 0.52 | no | 4.3 |
| `finlandization.fi` | fi | topic | 0.32 | 0.69 | no | 2.2 |
| `finlandization.broad` | broad | topic | 0.09 | 0.20 | no | 1.7 |
| `finlandization.sv` | sv | topic | 0.14 | 0.47 | no | 2.0 |
| `austerity-politics.en` | en | topic | 0.24 | 0.63 | no | 1.9 |
| `austerity-politics.fi` | fi | topic | 0.38 | 0.80 | no | 2.1 |
| `austerity-politics.broad` | broad | topic | 0.05 | 0.24 | no | 2.2 |
| `austerity-politics.sv` | sv | topic | 0.24 | 0.51 | no | 2.4 |
| `covid-19-finland.en` | en | topic | 0.33 | 0.45 | no | 2.1 |
| `covid-19-finland.fi` | fi | topic | 0.67 | 0.73 | no | 2.1 |
| `covid-19-finland.broad` | broad | topic | 0.08 | 0.09 | no | 2.2 |
| `covid-19-finland.sv` | sv | topic | 0.33 | 0.49 | no | 2.4 |
| `russification-independence.en` | en | topic | 0.25 | 0.66 | no | 2.0 |
| `russification-independence.fi` | fi | topic | 0.33 | 0.89 | no | 1.8 |
| `russification-independence.broad` | broad | topic | 0.12 | 0.28 | no | 1.8 |
| `russification-independence.sv` | sv | topic | 0.17 | 0.51 | no | 1.6 |
| `border-crisis-2023.en` | en | control-quiet | 0.46 | 0.80 | no | 1.7 |
| `border-crisis-2023.fi` | fi | control-quiet | 0.69 | 0.92 | no | 2.2 |
| `border-crisis-2023.broad` | broad | control-quiet | 0.46 | 0.77 | no | 2.2 |
| `border-crisis-2023.sv` | sv | control-quiet | 0.46 | 0.81 | no | 2.1 |
| `french-revolution.en` | en | control-out-of-corpus | – | – | no | 2.3 |
| `french-revolution.fi` | fi | control-out-of-corpus | – | – | no | 2.1 |
| `french-revolution.broad` | broad | control-out-of-corpus | – | – | no | 2.2 |

</details>

**Failures:** none.

**Cost:** 3.2 s per request (max 24.5 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --name r1-vector -- python3 research/8-r1-vector/variant.py
```

</details>
