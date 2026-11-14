retrieval variant 'r3-a': 43 request(s) on the dev split of golden 2026-11-14.1
[1/43] civil-war-1918.en: 12.0 s, ok
[2/43] civil-war-1918.fi: 12.0 s, ok
[3/43] civil-war-1918.broad: 13.6 s, ok
[4/43] civil-war-1918.sv: 12.5 s, ok
[5/43] continuation-war.en: 12.2 s, ok
[6/43] continuation-war.fi: 13.2 s, ok
[7/43] continuation-war.broad: 7.6 s, ok
[8/43] continuation-war.sv: 9.9 s, ok
[9/43] winter-war.en: 12.7 s, ok
[10/43] winter-war.fi: 10.9 s, ok
[11/43] winter-war.broad: 10.5 s, ok
[12/43] winter-war.sv: 10.2 s, ok
[13/43] ceded-karelia.en: 9.2 s, ok
[14/43] ceded-karelia.fi: 12.3 s, ok
[15/43] ceded-karelia.broad: 13.3 s, ok
[16/43] ceded-karelia.sv: 12.1 s, ok
[17/43] nato-debate.en: 10.6 s, ok
[18/43] nato-debate.fi: 14.2 s, ok
[19/43] nato-debate.broad: 13.4 s, ok
[20/43] nato-debate.sv: 15.0 s, ok
[21/43] finlandization.en: 13.8 s, ok
[22/43] finlandization.fi: 15.4 s, ok
[23/43] finlandization.broad: 11.5 s, ok
[24/43] finlandization.sv: 13.8 s, ok
[25/43] austerity-politics.en: 12.2 s, ok
[26/43] austerity-politics.fi: 12.0 s, ok
[27/43] austerity-politics.broad: 12.6 s, ok
[28/43] austerity-politics.sv: 13.7 s, ok
[29/43] covid-19-finland.en: 11.1 s, ok
[30/43] covid-19-finland.fi: 13.0 s, ok
[31/43] covid-19-finland.broad: 7.5 s, ok
[32/43] covid-19-finland.sv: 13.1 s, ok
[33/43] russification-independence.en: 8.2 s, ok
[34/43] russification-independence.fi: 7.5 s, ok
[35/43] russification-independence.broad: 13.3 s, ok
[36/43] russification-independence.sv: 10.5 s, ok
[37/43] border-crisis-2023.en: 10.3 s, ok
[38/43] border-crisis-2023.fi: 11.5 s, ok
[39/43] border-crisis-2023.broad: 14.1 s, ok
[40/43] border-crisis-2023.sv: 13.8 s, ok
[41/43] french-revolution.en: 8.5 s, ok
[42/43] french-revolution.fi: 9.6 s, ok
[43/43] french-revolution.broad: 13.2 s, ok
wrote research/24-r3-query/results/r3-a.dev.scorecard.json (+ .md)
### Eval scorecard · `r3-a` · retrieval · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 11 topics, 43 requests · commit `f4bb08596ee6` · 2026-11-14 15:56:37 UTC · 0 failed

| Metric | Value | Δ vs `r1-vector` |
|---|---:|---:|
| recall@5 | 0.22 | +0.03 |
| recall@10 | 0.33 | +0.05 |
| recall@20 | 0.46 | +0.07 |
| ndcg@10 | 0.63 | +0.09 |
| oos_detection_rate | 0.00 | 0.00 |
| oos_false_alarm_rate | 0.00 | 0.00 |
| seconds_per_topic | 11.80 | +8.59 |
| llm_calls | 43 | +43 |
| input_tokens | 71,239 | +71,239 |
| output_tokens | 23,162 | +23,162 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 10 | 0.25 | 0.38 | 0.52 | 0.70 |
| fi | 10 | 0.26 | 0.41 | 0.54 | 0.75 |
| broad | 10 | 0.14 | 0.21 | 0.33 | 0.39 |
| sv | 10 | 0.23 | 0.34 | 0.45 | 0.66 |

**5 best queries**

1. `border-crisis-2023.sv`: ndcg@10 0.94, recall@10 0.69; missed Rajavartiolaitos (grade 1), Vartius (grade 1), Raja-Jooseppi (ylikulkupaikka) (grade 1, rank 20), Suomen rajat (grade 1, rank 17)
2. `border-crisis-2023.fi`: ndcg@10 0.92, recall@10 0.69; missed Rajavartiolaitos (grade 1), Vartius (grade 1, rank 12), Raja-Jooseppi (ylikulkupaikka) (grade 1, rank 11), Suomen rajat (grade 1, rank 17)
3. `border-crisis-2023.en`: ndcg@10 0.92, recall@10 0.69; missed Rajavartiolaitos (grade 1), Vartius (grade 1, rank 13), Raja-Jooseppi (ylikulkupaikka) (grade 1, rank 14), Suomen rajat (grade 1)
4. `russification-independence.fi`: ndcg@10 0.90, recall@10 0.33; missed Helmikuun manifesti (grade 2), Suomen suuriruhtinaskunta (grade 1), Eugen Schauman (grade 1), Suuri adressi (grade 1), Franz Albert Seyn (grade 1)
5. `nato-debate.fi`: ndcg@10 0.81, recall@10 0.44; missed Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Luettelo Suomen historian sodista (grade 1), Suomettuminen (grade 1), Erkki Tuomioja (grade 1), Alexander Stubb (grade 1, rank 19)

**5 worst queries**

1. `nato-debate.broad`: recall@10 0.06, ndcg@10 0.07; missed Suomen Nato-keskustelu (grade 2, rank 17), Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2), Suomen Nato-jäsenyys (grade 2)
2. `austerity-politics.broad`: recall@10 0.10, ndcg@10 0.38; missed Juha Sipilä (grade 2), Kataisen hallitus (grade 2), Sipilän hallitus (grade 2, rank 13), Suomi 2010-luvulla (grade 2), Kilpailukykysopimus (grade 2)
3. `ceded-karelia.broad`: recall@10 0.10, ndcg@10 0.17; missed Heimosodat (grade 2), Karjala-kysymys (grade 2, rank 12), ProKarelia (grade 2), Suur-Suomi (grade 2), Itä-Karjalan kysymys (grade 2)
4. `civil-war-1918.broad`: recall@10 0.11, ndcg@10 0.28; missed Suomen sisällissota (grade 2), Punakaarti (grade 2), Valkoiset (grade 2, rank 16), Carl Gustaf Emil Mannerheim (grade 1), Suojeluskunta (grade 1)
5. `russification-independence.broad`: recall@10 0.12, ndcg@10 0.24; missed Jääkäriliike (grade 2), Sortovuodet (grade 2), Nikolai Bobrikov (grade 2), Helmikuun manifesti (grade 2), Sortokausien aktivismi (grade 2)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `civil-war-1918.en` | en | topic | 0.32 | 0.59 | no | 12.0 |
| `civil-war-1918.fi` | fi | topic | 0.32 | 0.59 | no | 12.1 |
| `civil-war-1918.broad` | broad | topic | 0.11 | 0.28 | no | 13.6 |
| `civil-war-1918.sv` | sv | topic | 0.26 | 0.52 | no | 12.5 |
| `continuation-war.en` | en | topic | 0.32 | 0.68 | no | 12.2 |
| `continuation-war.fi` | fi | topic | 0.32 | 0.71 | no | 13.2 |
| `continuation-war.broad` | broad | topic | 0.37 | 0.74 | no | 7.6 |
| `continuation-war.sv` | sv | topic | 0.32 | 0.74 | no | 9.9 |
| `winter-war.en` | en | topic | 0.36 | 0.75 | no | 12.8 |
| `winter-war.fi` | fi | topic | 0.36 | 0.73 | no | 10.9 |
| `winter-war.broad` | broad | topic | 0.14 | 0.19 | no | 10.5 |
| `winter-war.sv` | sv | topic | 0.36 | 0.73 | no | 10.2 |
| `ceded-karelia.en` | en | topic | 0.30 | 0.63 | no | 9.2 |
| `ceded-karelia.fi` | fi | topic | 0.30 | 0.69 | no | 12.3 |
| `ceded-karelia.broad` | broad | topic | 0.10 | 0.17 | no | 13.3 |
| `ceded-karelia.sv` | sv | topic | 0.30 | 0.54 | no | 12.1 |
| `nato-debate.en` | en | topic | 0.38 | 0.74 | no | 10.6 |
| `nato-debate.fi` | fi | topic | 0.44 | 0.81 | no | 14.2 |
| `nato-debate.broad` | broad | topic | 0.06 | 0.07 | no | 13.4 |
| `nato-debate.sv` | sv | topic | 0.38 | 0.74 | no | 15.0 |
| `finlandization.en` | en | topic | 0.27 | 0.65 | no | 13.8 |
| `finlandization.fi` | fi | topic | 0.27 | 0.64 | no | 15.4 |
| `finlandization.broad` | broad | topic | 0.18 | 0.34 | no | 11.4 |
| `finlandization.sv` | sv | topic | 0.23 | 0.59 | no | 13.8 |
| `austerity-politics.en` | en | topic | 0.29 | 0.65 | no | 12.2 |
| `austerity-politics.fi` | fi | topic | 0.38 | 0.80 | no | 12.0 |
| `austerity-politics.broad` | broad | topic | 0.10 | 0.38 | no | 12.6 |
| `austerity-politics.sv` | sv | topic | 0.33 | 0.75 | no | 13.7 |
| `covid-19-finland.en` | en | topic | 0.67 | 0.73 | no | 11.1 |
| `covid-19-finland.fi` | fi | topic | 0.67 | 0.73 | no | 13.0 |
| `covid-19-finland.broad` | broad | topic | 0.50 | 0.76 | no | 7.5 |
| `covid-19-finland.sv` | sv | topic | 0.42 | 0.66 | no | 13.1 |
| `russification-independence.en` | en | topic | 0.25 | 0.70 | no | 8.2 |
| `russification-independence.fi` | fi | topic | 0.33 | 0.90 | no | 7.5 |
| `russification-independence.broad` | broad | topic | 0.12 | 0.24 | no | 13.3 |
| `russification-independence.sv` | sv | topic | 0.12 | 0.39 | no | 10.5 |
| `border-crisis-2023.en` | en | control-quiet | 0.69 | 0.92 | no | 10.3 |
| `border-crisis-2023.fi` | fi | control-quiet | 0.69 | 0.92 | no | 11.5 |
| `border-crisis-2023.broad` | broad | control-quiet | 0.38 | 0.71 | no | 14.1 |
| `border-crisis-2023.sv` | sv | control-quiet | 0.69 | 0.94 | no | 13.8 |
| `french-revolution.en` | en | control-out-of-corpus | – | – | no | 8.5 |
| `french-revolution.fi` | fi | control-out-of-corpus | – | – | no | 9.6 |
| `french-revolution.broad` | broad | control-out-of-corpus | – | – | no | 13.2 |

</details>

**Failures:** none.

**Cost:** 11.8 s per request (max 15.4 s, 1 at a time), 43 LLM calls, 71,239 in / 23,162 out tokens, $0.59.

<details><summary>Command</summary>

```
research/eval/run retrieval --name r3-a -- python3 research/24-r3-query/variant.py --mode A
```

</details>

