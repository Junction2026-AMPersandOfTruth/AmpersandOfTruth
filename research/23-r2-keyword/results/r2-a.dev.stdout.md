retrieval variant 'r2-a': 43 request(s) on the dev split of golden 2026-11-14.1
[1/43] civil-war-1918.en: 4.2 s, ok
[2/43] civil-war-1918.fi: 1.1 s, ok
[3/43] civil-war-1918.broad: 1.0 s, ok
[4/43] civil-war-1918.sv: 0.8 s, ok
[5/43] continuation-war.en: 1.2 s, ok
[6/43] continuation-war.fi: 1.0 s, ok
[7/43] continuation-war.broad: 0.8 s, ok
[8/43] continuation-war.sv: 1.4 s, ok
[9/43] winter-war.en: 9.1 s, ok
[10/43] winter-war.fi: 0.9 s, ok
[11/43] winter-war.broad: 1.0 s, ok
[12/43] winter-war.sv: 0.9 s, ok
[13/43] ceded-karelia.en: 0.9 s, ok
[14/43] ceded-karelia.fi: 5.6 s, ok
[15/43] ceded-karelia.broad: 16.9 s, ok
[16/43] ceded-karelia.sv: 1.5 s, ok
[17/43] nato-debate.en: 1.0 s, ok
[18/43] nato-debate.fi: 10.0 s, ok
[19/43] nato-debate.broad: 4.1 s, ok
[20/43] nato-debate.sv: 0.9 s, ok
[21/43] finlandization.en: 1.0 s, ok
[22/43] finlandization.fi: 0.5 s, ok
[23/43] finlandization.broad: 0.9 s, ok
[24/43] finlandization.sv: 1.1 s, ok
[25/43] austerity-politics.en: 0.9 s, ok
[26/43] austerity-politics.fi: 0.4 s, ok
[27/43] austerity-politics.broad: 0.9 s, ok
[28/43] austerity-politics.sv: 1.2 s, ok
[29/43] covid-19-finland.en: 2.6 s, ok
[30/43] covid-19-finland.fi: 1.3 s, ok
[31/43] covid-19-finland.broad: 0.8 s, ok
[32/43] covid-19-finland.sv: 1.0 s, ok
[33/43] russification-independence.en: 0.8 s, ok
[34/43] russification-independence.fi: 1.2 s, ok
[35/43] russification-independence.broad: 5.6 s, ok
[36/43] russification-independence.sv: 4.2 s, ok
[37/43] border-crisis-2023.en: 4.6 s, ok
[38/43] border-crisis-2023.fi: 0.8 s, ok
[39/43] border-crisis-2023.broad: 0.8 s, ok
[40/43] border-crisis-2023.sv: 0.9 s, ok
[41/43] french-revolution.en: 1.1 s, ok
[42/43] french-revolution.fi: 0.5 s, ok
[43/43] french-revolution.broad: 1.2 s, ok
wrote research/23-r2-keyword/results/r2-a.dev.scorecard.json (+ .md)
### Eval scorecard · `r2-a` · retrieval · dev split

Golden `2026-11-14.1` (`m-2026-10`) · 11 topics, 43 requests · commit `f4bb08596ee6` · 2026-11-14 15:32:34 UTC · 0 failed

| Metric | Value | Δ vs `r1-vector` |
|---|---:|---:|
| recall@5 | 0.09 | -0.10 |
| recall@10 | 0.14 | -0.14 |
| recall@20 | 0.20 | -0.19 |
| ndcg@10 | 0.29 | -0.25 |
| oos_detection_rate | 0.00 | 0.00 |
| oos_false_alarm_rate | 0.00 | 0.00 |
| seconds_per_topic | 2.29 | -0.92 |
| llm_calls | 0 | 0 |
| input_tokens | 0 | 0 |
| output_tokens | 0 | 0 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 10 | 0.08 | 0.12 | 0.19 | 0.25 |
| fi | 10 | 0.17 | 0.27 | 0.40 | 0.53 |
| broad | 10 | 0.05 | 0.09 | 0.12 | 0.19 |
| sv | 10 | 0.05 | 0.07 | 0.09 | 0.18 |

**5 best queries**

1. `nato-debate.fi`: ndcg@10 0.79, recall@10 0.50; missed Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Natottuminen (grade 2), Luettelo Suomen historian sodista (grade 1), Erkki Tuomioja (grade 1), Alexander Stubb (grade 1)
2. `winter-war.sv`: ndcg@10 0.68, recall@10 0.36; missed Terijoen hallitus (grade 2), Moskovan rauhansopimus (1940) (grade 2), Raatteen tien taistelu (grade 1), Mannerheim-linja (grade 1), Kollaan taistelu (grade 1)
3. `covid-19-finland.fi`: ndcg@10 0.68, recall@10 0.50; missed Etäopetus koronaviruspandemian aikana Suomessa (grade 2), Suomen koronaviruspandemian aikajana (grade 2, rank 13), Koronavilkku (grade 2), Sanna Marin (grade 1), Kuntavaalit 2021 (grade 1)
4. `finlandization.fi`: ndcg@10 0.60, recall@10 0.23; missed YYA-sopimus (grade 2, rank 20), Yöpakkaset (grade 2), Suomen ja Venäjän suhteet (grade 2), Kansakunta rähmällään (grade 2), Paavo Lipponen (grade 1)
5. `russification-independence.fi`: ndcg@10 0.58, recall@10 0.25; missed Sortokausien aktivismi (grade 2, rank 16), Jääkäripataljoona 27 (grade 2), Ensimmäinen sortokausi (grade 2, rank 13), Toinen sortokausi (grade 2, rank 18), Suomen suuriruhtinaskunta (grade 1, rank 11)

**5 worst queries**

1. `border-crisis-2023.sv`: recall@10 0.00, ndcg@10 0.00; missed Suomen ja Venäjän raja (grade 2), Suomen ja Venäjän rajakriisi 2023 (grade 2), Suomen ja Venäjän rajan esteaita (grade 2), Rajavartiolaitos (grade 1), Vartius (grade 1)
2. `ceded-karelia.broad`: recall@10 0.00, ndcg@10 0.00; missed Heimosodat (grade 2), Luovutettu Karjala (grade 2), Karjala-kysymys (grade 2), ProKarelia (grade 2), Suur-Suomi (grade 2)
3. `ceded-karelia.sv`: recall@10 0.00, ndcg@10 0.00; missed Heimosodat (grade 2), Luovutettu Karjala (grade 2), Karjala-kysymys (grade 2), ProKarelia (grade 2), Suur-Suomi (grade 2)
4. `nato-debate.broad`: recall@10 0.00, ndcg@10 0.00; missed Suomen Nato-keskustelu (grade 2), Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2), Suomen Nato-jäsenyys (grade 2)
5. `nato-debate.sv`: recall@10 0.00, ndcg@10 0.00; missed Suomen Nato-keskustelu (grade 2), Suomi ISAF-kriisinhallintaoperaatiossa Afganistanissa (grade 2), Suomen Nato-jäsenhakemus (grade 2), Natottuminen (grade 2), Suomen Nato-jäsenyys (grade 2)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `civil-war-1918.en` | en | topic | 0.11 | 0.32 | no | 4.2 |
| `civil-war-1918.fi` | fi | topic | 0.21 | 0.53 | no | 1.1 |
| `civil-war-1918.broad` | broad | topic | 0.11 | 0.32 | no | 1.0 |
| `civil-war-1918.sv` | sv | topic | 0.05 | 0.27 | no | 0.8 |
| `continuation-war.en` | en | topic | 0.21 | 0.30 | no | 1.2 |
| `continuation-war.fi` | fi | topic | 0.16 | 0.40 | no | 1.0 |
| `continuation-war.broad` | broad | topic | 0.16 | 0.20 | no | 0.8 |
| `continuation-war.sv` | sv | topic | 0.11 | 0.23 | no | 1.4 |
| `winter-war.en` | en | topic | 0.14 | 0.33 | no | 9.1 |
| `winter-war.fi` | fi | topic | 0.21 | 0.46 | no | 0.9 |
| `winter-war.broad` | broad | topic | 0.14 | 0.33 | no | 1.1 |
| `winter-war.sv` | sv | topic | 0.36 | 0.68 | no | 0.9 |
| `ceded-karelia.en` | en | topic | 0.05 | 0.04 | no | 0.9 |
| `ceded-karelia.fi` | fi | topic | 0.25 | 0.50 | no | 5.6 |
| `ceded-karelia.broad` | broad | topic | 0.00 | 0.00 | no | 16.9 |
| `ceded-karelia.sv` | sv | topic | 0.00 | 0.00 | no | 1.5 |
| `nato-debate.en` | en | topic | 0.19 | 0.38 | no | 1.0 |
| `nato-debate.fi` | fi | topic | 0.50 | 0.79 | no | 10.0 |
| `nato-debate.broad` | broad | topic | 0.00 | 0.00 | no | 4.0 |
| `nato-debate.sv` | sv | topic | 0.00 | 0.00 | no | 0.9 |
| `finlandization.en` | en | topic | 0.09 | 0.22 | no | 1.0 |
| `finlandization.fi` | fi | topic | 0.23 | 0.60 | no | 0.5 |
| `finlandization.broad` | broad | topic | 0.09 | 0.12 | no | 0.9 |
| `finlandization.sv` | sv | topic | 0.05 | 0.15 | no | 1.1 |
| `austerity-politics.en` | en | topic | 0.19 | 0.52 | no | 0.9 |
| `austerity-politics.fi` | fi | topic | 0.19 | 0.56 | no | 0.4 |
| `austerity-politics.broad` | broad | topic | 0.05 | 0.24 | no | 0.9 |
| `austerity-politics.sv` | sv | topic | 0.10 | 0.38 | no | 1.2 |
| `covid-19-finland.en` | en | topic | 0.08 | 0.24 | no | 2.6 |
| `covid-19-finland.fi` | fi | topic | 0.50 | 0.68 | no | 1.3 |
| `covid-19-finland.broad` | broad | topic | 0.08 | 0.24 | no | 0.8 |
| `covid-19-finland.sv` | sv | topic | 0.08 | 0.08 | no | 1.0 |
| `russification-independence.en` | en | topic | 0.04 | 0.03 | no | 0.8 |
| `russification-independence.fi` | fi | topic | 0.25 | 0.58 | no | 1.2 |
| `russification-independence.broad` | broad | topic | 0.08 | 0.15 | no | 5.6 |
| `russification-independence.sv` | sv | topic | 0.00 | 0.00 | no | 4.2 |
| `border-crisis-2023.en` | en | control-quiet | 0.08 | 0.12 | no | 4.6 |
| `border-crisis-2023.fi` | fi | control-quiet | 0.15 | 0.24 | no | 0.8 |
| `border-crisis-2023.broad` | broad | control-quiet | 0.23 | 0.28 | no | 0.8 |
| `border-crisis-2023.sv` | sv | control-quiet | 0.00 | 0.00 | no | 0.9 |
| `french-revolution.en` | en | control-out-of-corpus | – | – | no | 1.1 |
| `french-revolution.fi` | fi | control-out-of-corpus | – | – | no | 0.5 |
| `french-revolution.broad` | broad | control-out-of-corpus | – | – | no | 1.1 |

</details>

**Failures:** none.

**Cost:** 2.3 s per request (max 16.9 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --name r2-a -- python3 research/23-r2-keyword/variant.py --mode A
```

</details>

