### Eval scorecard · `app-topicsearch` · retrieval · test split

Golden `2026-11-14.1` (`m-2026-10`) · 5 topics, 19 requests · commit `ef7717a6e07b` · 2026-11-14 21:44:28 UTC · 0 failed

| Metric | Value | Δ vs `r3-d` |
|---|---:|---:|
| recall@5 | 0.21 | 0.00 |
| recall@10 | 0.32 | 0.00 |
| recall@20 | 0.45 | 0.00 |
| ndcg@10 | 0.66 | 0.00 |
| oos_detection_rate | 1.00 | +1.00 |
| oos_false_alarm_rate | 0.00 | 0.00 |
| seconds_per_topic | 4.96 | -7.64 |
| llm_calls | 0 | -19 |
| input_tokens | 0 | -30,424 |
| output_tokens | 0 | -9,340 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 4 | 0.23 | 0.34 | 0.49 | 0.69 |
| fi | 4 | 0.24 | 0.35 | 0.49 | 0.70 |
| broad | 4 | 0.13 | 0.22 | 0.32 | 0.51 |
| sv | 4 | 0.26 | 0.37 | 0.51 | 0.74 |

**5 best queries**

1. `national-myths.sv`: ndcg@10 0.81, recall@10 0.40; missed Elias Lönnrot (grade 2), Piispa Henrikin surmavirsi (grade 2), Fennomania (grade 1), Kanteletar (grade 1, rank 11), Ensimmäinen ristiretki Suomeen (grade 1)
2. `national-myths.en`: ndcg@10 0.81, recall@10 0.40; missed Elias Lönnrot (grade 2), Piispa Henrikin surmavirsi (grade 2), Fennomania (grade 1), Kanteletar (grade 1, rank 16), Ensimmäinen ristiretki Suomeen (grade 1)
3. `sami-prehistory.sv`: ndcg@10 0.78, recall@10 0.41; missed Suomen muinaiset kuninkaat (grade 2, rank 18), Suomalaisten alkuperä (grade 2, rank 13), Lappalaiset (grade 1), Lapinkylä (grade 1), Suomen kronikka (grade 1)
4. `national-myths.fi`: ndcg@10 0.77, recall@10 0.35; missed Elias Lönnrot (grade 2, rank 13), Piispa Henrikin surmavirsi (grade 2), Fennomania (grade 1), Kanteletar (grade 1, rank 14), Ensimmäinen ristiretki Suomeen (grade 1)
5. `sami-prehistory.en`: ndcg@10 0.72, recall@10 0.35; missed Suomen muinaiset kuninkaat (grade 2), Suomalaisten alkuperä (grade 2, rank 14), Lappalaiset (grade 1), Fennit (grade 1, rank 12), Lapinkylä (grade 1)

**5 worst queries**

1. `far-right.en`: recall@10 0.17, ndcg@10 0.50; missed Suomen vastarintaliike (grade 2, rank 20), Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2, rank 14), Sinimusta liike (grade 2), Suomi – Isänmaa (grade 1)
2. `language-politics.broad`: recall@10 0.19, ndcg@10 0.31; missed Suomalainen puolue (grade 2), Suomalaisuuden liitto (grade 2, rank 14), Axel Olof Freudenthal (grade 2), Ahvenanmaan kysymys (grade 2), Vapaa kielivalinta (grade 2)
3. `far-right.broad`: recall@10 0.22, ndcg@10 0.60; missed Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2, rank 14), Sinimusta liike (grade 2), Isänmaallinen Kansanliike (1993) (grade 1), Vihtori Kosola (grade 1)
4. `sami-prehistory.broad`: recall@10 0.24, ndcg@10 0.53; missed Suomalaisten alkuperä (grade 2, rank 18), Saamelaisten historia (grade 2, rank 15), Metsäsaamelaiset (grade 2), Lappalaiset (grade 1), Lapinkylä (grade 1)
5. `national-myths.broad`: recall@10 0.25, ndcg@10 0.60; missed Elias Lönnrot (grade 2), Lalli (grade 2), Piispa Henrikin surmavirsi (grade 2), Fennomania (grade 1, rank 13), Piispa Henrik (grade 1)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `language-politics.en` | en | topic | 0.44 | 0.71 | no | 4.8 |
| `language-politics.fi` | fi | topic | 0.38 | 0.64 | no | 4.8 |
| `language-politics.broad` | broad | topic | 0.19 | 0.31 | no | 5.1 |
| `language-politics.sv` | sv | topic | 0.38 | 0.69 | no | 4.6 |
| `sami-prehistory.en` | en | topic | 0.35 | 0.72 | no | 5.0 |
| `sami-prehistory.fi` | fi | topic | 0.35 | 0.68 | no | 4.5 |
| `sami-prehistory.broad` | broad | topic | 0.24 | 0.53 | no | 5.5 |
| `sami-prehistory.sv` | sv | topic | 0.41 | 0.78 | no | 5.7 |
| `far-right.en` | en | topic | 0.17 | 0.50 | no | 4.9 |
| `far-right.fi` | fi | topic | 0.30 | 0.71 | no | 5.1 |
| `far-right.broad` | broad | topic | 0.22 | 0.60 | no | 4.6 |
| `far-right.sv` | sv | topic | 0.30 | 0.68 | no | 4.6 |
| `national-myths.en` | en | topic | 0.40 | 0.81 | no | 5.1 |
| `national-myths.fi` | fi | topic | 0.35 | 0.77 | no | 5.0 |
| `national-myths.broad` | broad | topic | 0.25 | 0.60 | no | 5.4 |
| `national-myths.sv` | sv | topic | 0.40 | 0.81 | no | 4.8 |
| `moon-landing-hoax.en` | en | control-out-of-corpus | – | – | yes | 5.0 |
| `moon-landing-hoax.fi` | fi | control-out-of-corpus | – | – | yes | 5.3 |
| `moon-landing-hoax.broad` | broad | control-out-of-corpus | – | – | yes | 4.4 |

</details>

**Failures:** none.

**Cost:** 5.0 s per request (max 5.7 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --split test --name app-topicsearch --timeout 300 -- scripts/investigate.sh variant
```

</details>
