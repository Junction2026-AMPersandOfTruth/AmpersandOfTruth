### Eval scorecard · `r1-vector` · retrieval · test split

Golden `2026-11-14.1` (`m-2026-10`) · 5 topics, 19 requests · commit `f985802eb562` · 2026-11-14 17:36:55 UTC · 0 failed

| Metric | Value |
|---|---:|
| recall@5 | 0.19 |
| recall@10 | 0.27 |
| recall@20 | 0.39 |
| ndcg@10 | 0.57 |
| oos_detection_rate | 0.00 |
| oos_false_alarm_rate | 0.00 |
| seconds_per_topic | 0.96 |
| llm_calls | 0 |
| input_tokens | 0 |
| output_tokens | 0 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 4 | 0.23 | 0.31 | 0.40 | 0.64 |
| fi | 4 | 0.19 | 0.31 | 0.50 | 0.62 |
| broad | 4 | 0.12 | 0.17 | 0.24 | 0.38 |
| sv | 4 | 0.21 | 0.31 | 0.42 | 0.66 |

**5 best queries**

1. `sami-prehistory.sv`: ndcg@10 0.83, recall@10 0.47; missed Suomalaisten alkuperä (grade 2), Lappalaiset (grade 1), Lapinkylä (grade 1), Suomen kronikka (grade 1), Keminsaamelaiset (grade 1)
2. `national-myths.sv`: ndcg@10 0.75, recall@10 0.35; missed Elias Lönnrot (grade 2), Piispa Henrikin surmavirsi (grade 2), Fennomania (grade 1), Piispa Henrik (grade 1), Kanteletar (grade 1, rank 12)
3. `national-myths.en`: ndcg@10 0.72, recall@10 0.30; missed Elias Lönnrot (grade 2, rank 19), Piispa Henrikin surmavirsi (grade 2), Fennomania (grade 1), Piispa Henrik (grade 1), Kanteletar (grade 1, rank 11)
4. `far-right.fi`: ndcg@10 0.69, recall@10 0.26; missed Suomen Kansa Ensin (grade 2), Sinimusta liike (grade 2, rank 20), Suomi – Isänmaa (grade 1), Isänmaallinen Kansanliike (1993) (grade 1, rank 11), Vihtori Kosola (grade 1)
5. `language-politics.fi`: ndcg@10 0.67, recall@10 0.38; missed Suomalainen puolue (grade 2), Suomalaisuuden liitto (grade 2), Ahvenanmaan kysymys (grade 2), Vapaa kielivalinta (grade 2), Jomalan kirkko (grade 1)

**5 worst queries**

1. `language-politics.broad`: recall@10 0.12, ndcg@10 0.32; missed Fennomania (grade 2), Suomalainen puolue (grade 2), Suomalaisuuden liitto (grade 2), Axel Olof Freudenthal (grade 2), Ahvenanmaan kysymys (grade 2)
2. `far-right.broad`: recall@10 0.17, ndcg@10 0.42; missed Isänmaallinen kansanliike (grade 2, rank 11), Suomen vastarintaliike (grade 2), Suomen Kansa Ensin (grade 2, rank 20), Kansallissosialismi Suomessa (grade 2), Sinimusta liike (grade 2, rank 14)
3. `sami-prehistory.broad`: recall@10 0.18, ndcg@10 0.46; missed Saamelaiset (grade 2), Suomalaisten alkuperä (grade 2), Saamelaisten historia (grade 2), Metsäsaamelaiset (grade 2), Lappalaiset (grade 1)
4. `language-politics.sv`: recall@10 0.19, ndcg@10 0.50; missed Suomalainen puolue (grade 2), Suomalaisuuden liitto (grade 2), Axel Olof Freudenthal (grade 2), Ahvenanmaan kysymys (grade 2), Vapaa kielivalinta (grade 2)
5. `national-myths.broad`: recall@10 0.20, ndcg@10 0.31; missed Kalevala (grade 2), Elias Lönnrot (grade 2), Lalli (grade 2), Piispa Henrikin surmavirsi (grade 2), Karelianismi (grade 2, rank 17)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `language-politics.en` | en | topic | 0.31 | 0.64 | no | 0.9 |
| `language-politics.fi` | fi | topic | 0.38 | 0.67 | no | 0.8 |
| `language-politics.broad` | broad | topic | 0.12 | 0.32 | no | 0.8 |
| `language-politics.sv` | sv | topic | 0.19 | 0.50 | no | 1.0 |
| `sami-prehistory.en` | en | topic | 0.41 | 0.59 | no | 0.7 |
| `sami-prehistory.fi` | fi | topic | 0.35 | 0.49 | no | 0.8 |
| `sami-prehistory.broad` | broad | topic | 0.18 | 0.46 | no | 0.9 |
| `sami-prehistory.sv` | sv | topic | 0.47 | 0.83 | no | 0.8 |
| `far-right.en` | en | topic | 0.22 | 0.60 | no | 0.9 |
| `far-right.fi` | fi | topic | 0.26 | 0.69 | no | 0.9 |
| `far-right.broad` | broad | topic | 0.17 | 0.42 | no | 0.9 |
| `far-right.sv` | sv | topic | 0.22 | 0.58 | no | 1.4 |
| `national-myths.en` | en | topic | 0.30 | 0.72 | no | 1.4 |
| `national-myths.fi` | fi | topic | 0.25 | 0.63 | no | 2.0 |
| `national-myths.broad` | broad | topic | 0.20 | 0.31 | no | 0.8 |
| `national-myths.sv` | sv | topic | 0.35 | 0.75 | no | 0.8 |
| `moon-landing-hoax.en` | en | control-out-of-corpus | – | – | no | 0.8 |
| `moon-landing-hoax.fi` | fi | control-out-of-corpus | – | – | no | 0.8 |
| `moon-landing-hoax.broad` | broad | control-out-of-corpus | – | – | no | 0.8 |

</details>

**Failures:** none.

**Cost:** 1.0 s per request (max 2.0 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --split test --name r1-vector -- python3 research/8-r1-vector/variant.py
```

</details>
