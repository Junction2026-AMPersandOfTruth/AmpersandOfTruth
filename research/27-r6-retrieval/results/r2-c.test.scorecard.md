### Eval scorecard · `r2-c` · retrieval · test split

Golden `2026-11-14.1` (`m-2026-10`) · 5 topics, 19 requests · commit `f985802eb562` · 2026-11-14 17:49:34 UTC · 0 failed

| Metric | Value |
|---|---:|
| recall@5 | 0.15 |
| recall@10 | 0.25 |
| recall@20 | 0.34 |
| ndcg@10 | 0.51 |
| oos_detection_rate | 0.00 |
| oos_false_alarm_rate | 0.00 |
| seconds_per_topic | 1.41 |
| llm_calls | 0 |
| input_tokens | 0 |
| output_tokens | 0 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 4 | 0.19 | 0.31 | 0.37 | 0.60 |
| fi | 4 | 0.22 | 0.35 | 0.49 | 0.71 |
| broad | 4 | 0.05 | 0.13 | 0.19 | 0.22 |
| sv | 4 | 0.16 | 0.21 | 0.30 | 0.51 |

**5 best queries**

1. `national-myths.fi`: ndcg@10 0.83, recall@10 0.40; missed Piispa Henrikin surmavirsi (grade 2), Fennomania (grade 1), Kalevalainen runous (grade 1, rank 15), Ensimmäinen ristiretki Suomeen (grade 1), Vienan Karjala (grade 1)
2. `far-right.fi`: ndcg@10 0.82, recall@10 0.39; missed Suomen vastarintaliike (grade 2, rank 11), Suomen Kansa Ensin (grade 2), Sinimusta liike (grade 2, rank 13), Suomi – Isänmaa (grade 1), Vihtori Kosola (grade 1, rank 12)
3. `far-right.en`: ndcg@10 0.70, recall@10 0.30; missed Suomen vastarintaliike (grade 2), Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2), Suomi – Isänmaa (grade 1), Vihtori Kosola (grade 1, rank 13)
4. `sami-prehistory.sv`: ndcg@10 0.70, recall@10 0.29; missed Suomen muinaiset kuninkaat (grade 2, rank 16), Suomalaisten alkuperä (grade 2), Lappalaiset (grade 1), Fennit (grade 1, rank 13), Lapinkylä (grade 1)
5. `sami-prehistory.fi`: ndcg@10 0.64, recall@10 0.29; missed Suomen muinaiset kuninkaat (grade 2), Suomen esihistoria (grade 2), Metsäsaamelaiset (grade 2), Lappalaiset (grade 1, rank 15), Fennit (grade 1)

**5 worst queries**

1. `language-politics.broad`: recall@10 0.06, ndcg@10 0.07; missed Fennomania (grade 2), Suomalainen puolue (grade 2), Svekomania (grade 2, rank 17), Suomalaisuuden liitto (grade 2), Axel Olof Freudenthal (grade 2)
2. `far-right.broad`: recall@10 0.09, ndcg@10 0.29; missed Lapuanliike (grade 2, rank 12), Isänmaallinen kansanliike (grade 2, rank 20), Suomen vastarintaliike (grade 2), Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2)
3. `language-politics.sv`: recall@10 0.12, ndcg@10 0.31; missed Fennomania (grade 2, rank 12), Suomalainen puolue (grade 2), Suomalaisuuden liitto (grade 2), Axel Olof Freudenthal (grade 2), Ahvenanmaan kysymys (grade 2)
4. `national-myths.broad`: recall@10 0.15, ndcg@10 0.22; missed Elias Lönnrot (grade 2), Lalli (grade 2), Piispa Henrikin surmavirsi (grade 2), Karelianismi (grade 2), Fennomania (grade 1)
5. `national-myths.sv`: recall@10 0.15, ndcg@10 0.43; missed Elias Lönnrot (grade 2), Lalli (grade 2, rank 18), Piispa Henrikin surmavirsi (grade 2), Fennomania (grade 1), Piispa Henrik (grade 1)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `language-politics.en` | en | topic | 0.38 | 0.45 | no | 1.2 |
| `language-politics.fi` | fi | topic | 0.31 | 0.55 | no | 1.4 |
| `language-politics.broad` | broad | topic | 0.06 | 0.07 | no | 1.3 |
| `language-politics.sv` | sv | topic | 0.12 | 0.31 | no | 1.6 |
| `sami-prehistory.en` | en | topic | 0.29 | 0.61 | no | 1.2 |
| `sami-prehistory.fi` | fi | topic | 0.29 | 0.64 | no | 1.6 |
| `sami-prehistory.broad` | broad | topic | 0.24 | 0.31 | no | 1.9 |
| `sami-prehistory.sv` | sv | topic | 0.29 | 0.70 | no | 1.6 |
| `far-right.en` | en | topic | 0.30 | 0.70 | no | 1.4 |
| `far-right.fi` | fi | topic | 0.39 | 0.82 | no | 1.2 |
| `far-right.broad` | broad | topic | 0.09 | 0.29 | no | 1.3 |
| `far-right.sv` | sv | topic | 0.26 | 0.60 | no | 1.3 |
| `national-myths.en` | en | topic | 0.25 | 0.63 | no | 2.1 |
| `national-myths.fi` | fi | topic | 0.40 | 0.83 | no | 1.2 |
| `national-myths.broad` | broad | topic | 0.15 | 0.22 | no | 1.2 |
| `national-myths.sv` | sv | topic | 0.15 | 0.43 | no | 1.2 |
| `moon-landing-hoax.en` | en | control-out-of-corpus | – | – | no | 1.2 |
| `moon-landing-hoax.fi` | fi | control-out-of-corpus | – | – | no | 1.2 |
| `moon-landing-hoax.broad` | broad | control-out-of-corpus | – | – | no | 1.6 |

</details>

**Failures:** none.

**Cost:** 1.4 s per request (max 2.1 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --split test --name r2-c -- python3 research/23-r2-keyword/variant.py --mode C
```

</details>
