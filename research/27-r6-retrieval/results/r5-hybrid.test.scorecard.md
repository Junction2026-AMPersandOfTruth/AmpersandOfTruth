### Eval scorecard · `r5-hybrid` · retrieval · test split

Golden `2026-11-14.1` (`m-2026-10`) · 5 topics, 19 requests · commit `f985802eb562` · 2026-11-14 17:50:15 UTC · 0 failed

| Metric | Value |
|---|---:|
| recall@5 | 0.14 |
| recall@10 | 0.21 |
| recall@20 | 0.33 |
| ndcg@10 | 0.50 |
| oos_detection_rate | 0.00 |
| oos_false_alarm_rate | 0.00 |
| seconds_per_topic | 1.83 |
| llm_calls | 0 |
| input_tokens | 0 |
| output_tokens | 0 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 4 | 0.16 | 0.23 | 0.38 | 0.57 |
| fi | 4 | 0.15 | 0.23 | 0.39 | 0.57 |
| broad | 4 | 0.09 | 0.16 | 0.22 | 0.35 |
| sv | 4 | 0.15 | 0.20 | 0.33 | 0.52 |

**5 best queries**

1. `sami-prehistory.fi`: ndcg@10 0.83, recall@10 0.41; missed Lappalaiset (grade 1), Lapinkylä (grade 1), Suomen kronikka (grade 1), Saamelaisten kotiseutualue (grade 1, rank 15), Keminsaamelaiset (grade 1)
2. `sami-prehistory.sv`: ndcg@10 0.77, recall@10 0.35; missed Metsäsaamelaiset (grade 2, rank 18), Lappalaiset (grade 1), Lapinkylä (grade 1), Suomen kronikka (grade 1), Saamelaisten kotiseutualue (grade 1)
3. `sami-prehistory.en`: ndcg@10 0.77, recall@10 0.35; missed Metsäsaamelaiset (grade 2, rank 20), Lappalaiset (grade 1), Lapinkylä (grade 1), Suomen kronikka (grade 1), Saamelaisten kotiseutualue (grade 1, rank 14)
4. `national-myths.en`: ndcg@10 0.63, recall@10 0.25; missed Piispa Henrikin surmavirsi (grade 2), Fennomania (grade 1), Piispa Henrik (grade 1), Kanteletar (grade 1), Ensimmäinen ristiretki Suomeen (grade 1)
5. `national-myths.fi`: ndcg@10 0.56, recall@10 0.20; missed Piispa Henrikin surmavirsi (grade 2), Karelianismi (grade 2, rank 14), Fennomania (grade 1), Piispa Henrik (grade 1), Kanteletar (grade 1)

**5 worst queries**

1. `language-politics.sv`: recall@10 0.12, ndcg@10 0.35; missed Suomalainen puolue (grade 2), Suomalaisuuden liitto (grade 2), Axel Olof Freudenthal (grade 2), Ahvenanmaan kysymys (grade 2), Kielitaistelu (grade 2, rank 15)
2. `far-right.en`: recall@10 0.13, ndcg@10 0.39; missed Lapuanliike (grade 2, rank 11), Suomen vastarintaliike (grade 2), Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2), Sinimusta liike (grade 2)
3. `far-right.fi`: recall@10 0.13, ndcg@10 0.41; missed Lapuanliike (grade 2, rank 11), Suomen vastarintaliike (grade 2, rank 17), Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2, rank 20), Sinimusta liike (grade 2)
4. `far-right.sv`: recall@10 0.13, ndcg@10 0.41; missed Lapuanliike (grade 2, rank 11), Suomen vastarintaliike (grade 2), Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2), Sinimusta liike (grade 2)
5. `far-right.broad`: recall@10 0.13, ndcg@10 0.49; missed Lapuanliike (grade 2, rank 19), Suomen vastarintaliike (grade 2), Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2), Suomi – Isänmaa (grade 1, rank 13)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `language-politics.en` | en | topic | 0.19 | 0.49 | no | 1.3 |
| `language-politics.fi` | fi | topic | 0.19 | 0.49 | no | 1.1 |
| `language-politics.broad` | broad | topic | 0.19 | 0.25 | no | 1.2 |
| `language-politics.sv` | sv | topic | 0.12 | 0.35 | no | 1.4 |
| `sami-prehistory.en` | en | topic | 0.35 | 0.77 | no | 2.9 |
| `sami-prehistory.fi` | fi | topic | 0.41 | 0.83 | no | 1.1 |
| `sami-prehistory.broad` | broad | topic | 0.18 | 0.48 | no | 1.8 |
| `sami-prehistory.sv` | sv | topic | 0.35 | 0.77 | no | 2.6 |
| `far-right.en` | en | topic | 0.13 | 0.39 | no | 3.2 |
| `far-right.fi` | fi | topic | 0.13 | 0.41 | no | 3.2 |
| `far-right.broad` | broad | topic | 0.13 | 0.49 | no | 1.8 |
| `far-right.sv` | sv | topic | 0.13 | 0.41 | no | 2.4 |
| `national-myths.en` | en | topic | 0.25 | 0.63 | no | 1.1 |
| `national-myths.fi` | fi | topic | 0.20 | 0.56 | no | 1.1 |
| `national-myths.broad` | broad | topic | 0.15 | 0.20 | no | 1.0 |
| `national-myths.sv` | sv | topic | 0.20 | 0.55 | no | 1.3 |
| `moon-landing-hoax.en` | en | control-out-of-corpus | – | – | no | 3.5 |
| `moon-landing-hoax.fi` | fi | control-out-of-corpus | – | – | no | 1.7 |
| `moon-landing-hoax.broad` | broad | control-out-of-corpus | – | – | no | 1.2 |

</details>

**Failures:** none.

**Cost:** 1.8 s per request (max 3.5 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --split test --name r5-hybrid -- python3 research/26-r5-claims/variant.py --mode hybrid
```

</details>
