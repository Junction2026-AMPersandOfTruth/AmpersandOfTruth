retrieval variant 'r5-hybrid-nograffiti': 43 request(s) on the dev split of golden 2026-11-14.1
[1/43] civil-war-1918.en: 2.7 s, ok
[2/43] civil-war-1918.fi: 4.5 s, ok
[3/43] civil-war-1918.broad: 22.7 s, ok
[4/43] civil-war-1918.sv: 33.1 s, ok
[5/43] continuation-war.en: 23.0 s, ok
[6/43] continuation-war.fi: 21.5 s, ok
[7/43] continuation-war.broad: 18.2 s, ok
[8/43] continuation-war.sv: 2.7 s, ok
[9/43] winter-war.en: 2.4 s, ok
[10/43] winter-war.fi: 1.5 s, ok
[11/43] winter-war.broad: 1.3 s, ok
[12/43] winter-war.sv: 1.5 s, ok
[13/43] ceded-karelia.en: 1.3 s, ok
[14/43] ceded-karelia.fi: 1.4 s, ok
[15/43] ceded-karelia.broad: 1.2 s, ok
[16/43] ceded-karelia.sv: 1.8 s, ok
[17/43] nato-debate.en: 1.4 s, ok
[18/43] nato-debate.fi: 1.5 s, ok
[19/43] nato-debate.broad: 1.3 s, ok
[20/43] nato-debate.sv: 2.9 s, ok
[21/43] finlandization.en: 1.3 s, ok
[22/43] finlandization.fi: 1.4 s, ok
[23/43] finlandization.broad: 2.6 s, ok
[24/43] finlandization.sv: 1.4 s, ok
[25/43] austerity-politics.en: 2.5 s, ok
[26/43] austerity-politics.fi: 1.9 s, ok
[27/43] austerity-politics.broad: 1.8 s, ok
[28/43] austerity-politics.sv: 1.3 s, ok
[29/43] covid-19-finland.en: 2.6 s, ok
[30/43] covid-19-finland.fi: 1.6 s, ok
[31/43] covid-19-finland.broad: 1.7 s, ok
[32/43] covid-19-finland.sv: 2.2 s, ok
[33/43] russification-independence.en: 1.4 s, ok
[34/43] russification-independence.fi: 1.6 s, ok
[35/43] russification-independence.broad: 1.5 s, ok
[36/43] russification-independence.sv: 2.4 s, ok
[37/43] border-crisis-2023.en: 1.4 s, ok
[38/43] border-crisis-2023.fi: 2.5 s, ok
[39/43] border-crisis-2023.broad: 2.1 s, ok
[40/43] border-crisis-2023.sv: 1.6 s, ok
[41/43] french-revolution.en: 1.5 s, ok
[42/43] french-revolution.fi: 3.2 s, ok
[43/43] french-revolution.broad: 1.3 s, ok
wrote research/26-r5-claims/results/r5-hybrid-nograffiti.dev.scorecard.json (+ .md)
### Eval scorecard · `r5-hybrid-nograffiti` · retrieval · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 11 topics, 43 requests · commit `f4bb08596ee6` · 2026-11-14 15:52:27 UTC · 0 failed

| Metric | Value | Δ vs `r1-vector` |
|---|---:|---:|
| recall@5 | 0.12 | -0.07 |
| recall@10 | 0.20 | -0.08 |
| recall@20 | 0.33 | -0.06 |
| ndcg@10 | 0.41 | -0.13 |
| oos_detection_rate | 0.00 | 0.00 |
| oos_false_alarm_rate | 0.00 | 0.00 |
| seconds_per_topic | 4.43 | +1.22 |
| llm_calls | 0 | 0 |
| input_tokens | 0 | 0 |
| output_tokens | 0 | 0 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 10 | 0.11 | 0.21 | 0.35 | 0.44 |
| fi | 10 | 0.16 | 0.24 | 0.42 | 0.52 |
| broad | 10 | 0.09 | 0.15 | 0.24 | 0.30 |
| sv | 10 | 0.12 | 0.20 | 0.30 | 0.38 |

**5 best queries**

1. `austerity-politics.fi`: ndcg@10 0.73, recall@10 0.29; missed Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2), Aktiivimalli (grade 2), Matti Vanhanen (grade 1), Osmo Soininvaara (grade 1)
2. `austerity-politics.en`: ndcg@10 0.68, recall@10 0.24; missed Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2), Aktiivimalli (grade 2), Matti Vanhanen (grade 1), Osmo Soininvaara (grade 1)
3. `winter-war.fi`: ndcg@10 0.66, recall@10 0.36; missed Talvisodan tausta (grade 2), Raatteen tien taistelu (grade 1), Simo Häyhä (grade 1), Mannerheim-linja (grade 1), Kollaan taistelu (grade 1)
4. `border-crisis-2023.fi`: ndcg@10 0.56, recall@10 0.31; missed Rajavartiolaitos (grade 1), Vartius (grade 1), Nuijamaan raja-asema (grade 1, rank 14), Raja-Jooseppi (ylikulkupaikka) (grade 1), Vaalimaan raja-asema (grade 1, rank 12)
5. `covid-19-finland.fi`: ndcg@10 0.55, recall@10 0.33; missed Etäopetus koronaviruspandemian aikana Suomessa (grade 2, rank 14), Suomen koronaviruspandemian aikajana (grade 2, rank 11), Suomen koronavirusstrategia (grade 2, rank 12), Koronavilkku (grade 2), Koronaviruspandemia Ahvenanmaalla (grade 2, rank 16)

**5 worst queries**

1. `russification-independence.broad`: recall@10 0.04, ndcg@10 0.09; missed Jääkäriliike (grade 2), Sortovuodet (grade 2, rank 16), Nikolai Bobrikov (grade 2), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2)
2. `continuation-war.sv`: recall@10 0.05, ndcg@10 0.24; missed Risto Ryti (grade 2), Sotasyyllisyysoikeudenkäynti (grade 2), Erillissotateesi (grade 2, rank 13), Suomen ja Saksan lähentyminen 1940–1941 (grade 2), Suomen ja Saksan sotilasyhteistyö 1940–1941 (grade 2)
3. `nato-debate.broad`: recall@10 0.06, ndcg@10 0.13; missed Suomen Nato-keskustelu (grade 2), Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2), Suomen Nato-jäsenyys (grade 2)
4. `russification-independence.sv`: recall@10 0.08, ndcg@10 0.17; missed Sortovuodet (grade 2, rank 15), Nikolai Bobrikov (grade 2), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2, rank 12), Jääkäripataljoona 27 (grade 2)
5. `russification-independence.en`: recall@10 0.08, ndcg@10 0.30; missed Sortovuodet (grade 2, rank 12), Nikolai Bobrikov (grade 2, rank 15), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2), Jääkäripataljoona 27 (grade 2)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `civil-war-1918.en` | en | topic | 0.16 | 0.42 | no | 2.7 |
| `civil-war-1918.fi` | fi | topic | 0.16 | 0.48 | no | 4.5 |
| `civil-war-1918.broad` | broad | topic | 0.26 | 0.47 | no | 22.7 |
| `civil-war-1918.sv` | sv | topic | 0.11 | 0.38 | no | 33.1 |
| `continuation-war.en` | en | topic | 0.11 | 0.32 | no | 23.0 |
| `continuation-war.fi` | fi | topic | 0.11 | 0.32 | no | 21.5 |
| `continuation-war.broad` | broad | topic | 0.21 | 0.41 | no | 18.2 |
| `continuation-war.sv` | sv | topic | 0.05 | 0.24 | no | 2.7 |
| `winter-war.en` | en | topic | 0.21 | 0.46 | no | 2.4 |
| `winter-war.fi` | fi | topic | 0.36 | 0.66 | no | 1.5 |
| `winter-war.broad` | broad | topic | 0.21 | 0.47 | no | 1.3 |
| `winter-war.sv` | sv | topic | 0.21 | 0.46 | no | 1.5 |
| `ceded-karelia.en` | en | topic | 0.25 | 0.51 | no | 1.3 |
| `ceded-karelia.fi` | fi | topic | 0.25 | 0.48 | no | 1.4 |
| `ceded-karelia.broad` | broad | topic | 0.10 | 0.26 | no | 1.1 |
| `ceded-karelia.sv` | sv | topic | 0.25 | 0.35 | no | 1.9 |
| `nato-debate.en` | en | topic | 0.38 | 0.40 | no | 1.4 |
| `nato-debate.fi` | fi | topic | 0.25 | 0.49 | no | 1.5 |
| `nato-debate.broad` | broad | topic | 0.06 | 0.13 | no | 1.3 |
| `nato-debate.sv` | sv | topic | 0.31 | 0.49 | no | 2.9 |
| `finlandization.en` | en | topic | 0.14 | 0.43 | no | 1.3 |
| `finlandization.fi` | fi | topic | 0.18 | 0.54 | no | 1.4 |
| `finlandization.broad` | broad | topic | 0.14 | 0.34 | no | 2.6 |
| `finlandization.sv` | sv | topic | 0.14 | 0.45 | no | 1.4 |
| `austerity-politics.en` | en | topic | 0.24 | 0.68 | no | 2.5 |
| `austerity-politics.fi` | fi | topic | 0.29 | 0.73 | no | 1.9 |
| `austerity-politics.broad` | broad | topic | 0.10 | 0.15 | no | 1.8 |
| `austerity-politics.sv` | sv | topic | 0.19 | 0.41 | no | 1.3 |
| `covid-19-finland.en` | en | topic | 0.25 | 0.39 | no | 2.6 |
| `covid-19-finland.fi` | fi | topic | 0.33 | 0.55 | no | 1.6 |
| `covid-19-finland.broad` | broad | topic | 0.17 | 0.29 | no | 1.7 |
| `covid-19-finland.sv` | sv | topic | 0.33 | 0.36 | no | 2.2 |
| `russification-independence.en` | en | topic | 0.08 | 0.30 | no | 1.4 |
| `russification-independence.fi` | fi | topic | 0.12 | 0.38 | no | 1.6 |
| `russification-independence.broad` | broad | topic | 0.04 | 0.09 | no | 1.5 |
| `russification-independence.sv` | sv | topic | 0.08 | 0.17 | no | 2.4 |
| `border-crisis-2023.en` | en | control-quiet | 0.31 | 0.54 | no | 1.4 |
| `border-crisis-2023.fi` | fi | control-quiet | 0.31 | 0.56 | no | 2.5 |
| `border-crisis-2023.broad` | broad | control-quiet | 0.23 | 0.44 | no | 2.1 |
| `border-crisis-2023.sv` | sv | control-quiet | 0.31 | 0.53 | no | 1.6 |
| `french-revolution.en` | en | control-out-of-corpus | – | – | no | 1.5 |
| `french-revolution.fi` | fi | control-out-of-corpus | – | – | no | 3.2 |
| `french-revolution.broad` | broad | control-out-of-corpus | – | – | no | 1.3 |

</details>

**Failures:** none.

**Cost:** 4.4 s per request (max 33.1 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --name r5-hybrid-nograffiti -- python3 research/26-r5-claims/variant.py --mode hybrid --exclude-classes graffiti_cleanup
```

</details>

