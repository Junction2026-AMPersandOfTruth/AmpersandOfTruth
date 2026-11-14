### Eval scorecard · `r4-d` · retrieval · test split

Golden `2026-11-14.1` (`m-2026-10`) · 5 topics, 19 requests · commit `f985802eb562` · 2026-11-14 17:50:01 UTC · 0 failed

| Metric | Value |
|---|---:|
| recall@5 | 0.15 |
| recall@10 | 0.24 |
| recall@20 | 0.37 |
| ndcg@10 | 0.57 |
| oos_detection_rate | 0.00 |
| oos_false_alarm_rate | 0.00 |
| seconds_per_topic | 0.74 |
| llm_calls | 0 |
| input_tokens | 0 |
| output_tokens | 0 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 4 | 0.17 | 0.28 | 0.40 | 0.64 |
| fi | 4 | 0.16 | 0.29 | 0.45 | 0.68 |
| broad | 4 | 0.10 | 0.15 | 0.24 | 0.40 |
| sv | 4 | 0.15 | 0.24 | 0.40 | 0.58 |

**5 best queries**

1. `sami-prehistory.fi`: ndcg@10 0.87, recall@10 0.41; missed Lappalaiset (grade 1, rank 12), Fennit (grade 1, rank 19), Lapinkylä (grade 1), Suomen kronikka (grade 1), Keminsaamelaiset (grade 1, rank 17)
2. `sami-prehistory.en`: ndcg@10 0.85, recall@10 0.47; missed Suomalaisten alkuperä (grade 2), Lappalaiset (grade 1), Lapinkylä (grade 1, rank 14), Suomen kronikka (grade 1), Keminsaamelaiset (grade 1)
3. `far-right.fi`: ndcg@10 0.76, recall@10 0.30; missed Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2), Suomi – Isänmaa (grade 1), Isänmaallinen Kansanliike (1993) (grade 1, rank 14), Vihtori Kosola (grade 1)
4. `sami-prehistory.sv`: ndcg@10 0.73, recall@10 0.35; missed Suomalaisten alkuperä (grade 2, rank 11), Lappalaiset (grade 1), Lapinkylä (grade 1), Suomen kronikka (grade 1), Saamelaisten kotiseutualue (grade 1, rank 20)
5. `far-right.broad`: ndcg@10 0.70, recall@10 0.26; missed Suomen vastarintaliike (grade 2), Kansallissosialismi Suomessa (grade 2), Isänmaallinen Kansanliike (1993) (grade 1, rank 18), Vihtori Kosola (grade 1), Suomen Kansan Järjestö (grade 1, rank 20)

**5 worst queries**

1. `language-politics.broad`: recall@10 0.06, ndcg@10 0.08; missed Fennomania (grade 2), Suomalainen puolue (grade 2), Svekomania (grade 2, rank 11), Axel Olof Freudenthal (grade 2), Ahvenanmaan kysymys (grade 2)
2. `national-myths.broad`: recall@10 0.10, ndcg@10 0.33; missed Elias Lönnrot (grade 2), Lalli (grade 2), Piispa Henrikin surmavirsi (grade 2), Karelianismi (grade 2), Fennomania (grade 1, rank 12)
3. `language-politics.sv`: recall@10 0.12, ndcg@10 0.38; missed Suomalainen puolue (grade 2), Suomalaisuuden liitto (grade 2), Axel Olof Freudenthal (grade 2), Ahvenanmaan kysymys (grade 2), Kielitaistelu (grade 2, rank 12)
4. `sami-prehistory.broad`: recall@10 0.18, ndcg@10 0.47; missed Saamelaiset (grade 2), Suomalaisten alkuperä (grade 2), Saamelaisten historia (grade 2), Metsäsaamelaiset (grade 2), Lappalaiset (grade 1)
5. `language-politics.en`: recall@10 0.19, ndcg@10 0.42; missed Suomalainen puolue (grade 2), Suomalaisuuden liitto (grade 2), Axel Olof Freudenthal (grade 2), Kielitaistelu (grade 2, rank 16), Vapaa kielivalinta (grade 2)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `language-politics.en` | en | topic | 0.19 | 0.42 | no | 0.7 |
| `language-politics.fi` | fi | topic | 0.19 | 0.47 | no | 0.7 |
| `language-politics.broad` | broad | topic | 0.06 | 0.08 | no | 0.7 |
| `language-politics.sv` | sv | topic | 0.12 | 0.38 | no | 0.6 |
| `sami-prehistory.en` | en | topic | 0.47 | 0.85 | no | 0.6 |
| `sami-prehistory.fi` | fi | topic | 0.41 | 0.87 | no | 0.7 |
| `sami-prehistory.broad` | broad | topic | 0.18 | 0.47 | no | 0.6 |
| `sami-prehistory.sv` | sv | topic | 0.35 | 0.73 | no | 0.7 |
| `far-right.en` | en | topic | 0.26 | 0.67 | no | 1.0 |
| `far-right.fi` | fi | topic | 0.30 | 0.76 | no | 1.4 |
| `far-right.broad` | broad | topic | 0.26 | 0.70 | no | 1.4 |
| `far-right.sv` | sv | topic | 0.22 | 0.58 | no | 0.8 |
| `national-myths.en` | en | topic | 0.20 | 0.61 | no | 0.6 |
| `national-myths.fi` | fi | topic | 0.25 | 0.61 | no | 0.6 |
| `national-myths.broad` | broad | topic | 0.10 | 0.33 | no | 0.6 |
| `national-myths.sv` | sv | topic | 0.25 | 0.64 | no | 0.6 |
| `moon-landing-hoax.en` | en | control-out-of-corpus | – | – | no | 0.8 |
| `moon-landing-hoax.fi` | fi | control-out-of-corpus | – | – | no | 0.6 |
| `moon-landing-hoax.broad` | broad | control-out-of-corpus | – | – | no | 0.6 |

</details>

**Failures:** none.

**Cost:** 0.7 s per request (max 1.4 s, 1 at a time), 0 LLM calls, 0 in / 0 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --split test --name r4-d -- python3 research/25-r4-expand/variant.py --mode D --w 0.05
```

</details>
