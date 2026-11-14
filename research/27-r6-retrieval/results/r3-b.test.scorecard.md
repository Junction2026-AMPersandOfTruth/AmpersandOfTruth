### Eval scorecard · `r3-b` · retrieval · test split

Golden `2026-11-14.1` (`m-2026-10`) · 5 topics, 19 requests · commit `f985802eb562` · 2026-11-14 17:46:39 UTC · 0 failed

| Metric | Value |
|---|---:|
| recall@5 | 0.18 |
| recall@10 | 0.28 |
| recall@20 | 0.43 |
| ndcg@10 | 0.56 |
| oos_detection_rate | 0.00 |
| oos_false_alarm_rate | 0.00 |
| seconds_per_topic | 9.18 |
| llm_calls | 19 |
| input_tokens | 30,424 |
| output_tokens | 9,340 |

**By query style**

| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |
|---|---:|---:|---:|---:|---:|
| en | 4 | 0.20 | 0.31 | 0.48 | 0.59 |
| fi | 4 | 0.20 | 0.31 | 0.48 | 0.62 |
| broad | 4 | 0.11 | 0.18 | 0.29 | 0.39 |
| sv | 4 | 0.23 | 0.32 | 0.47 | 0.64 |

**5 best queries**

1. `far-right.fi`: ndcg@10 0.69, recall@10 0.26; missed Suomen vastarintaliike (grade 2), Suomen Kansa Ensin (grade 2), Sinimusta liike (grade 2), Suomi – Isänmaa (grade 1), Vihtori Kosola (grade 1, rank 17)
2. `sami-prehistory.sv`: ndcg@10 0.68, recall@10 0.35; missed Suomen muinaiset kuninkaat (grade 2, rank 17), Suomen esihistoria (grade 2), Suomalaisten alkuperä (grade 2, rank 12), Lappalaiset (grade 1), Lapinkylä (grade 1)
3. `national-myths.sv`: ndcg@10 0.64, recall@10 0.35; missed Elias Lönnrot (grade 2), Piispa Henrikin surmavirsi (grade 2), Fennomania (grade 1), Piispa Henrik (grade 1), Ensimmäinen ristiretki Suomeen (grade 1)
4. `far-right.sv`: ndcg@10 0.64, recall@10 0.26; missed Suomen vastarintaliike (grade 2, rank 12), Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2, rank 18), Sinimusta liike (grade 2), Suomi – Isänmaa (grade 1)
5. `language-politics.fi`: ndcg@10 0.64, recall@10 0.38; missed Suomalainen puolue (grade 2, rank 17), Suomalaisuuden liitto (grade 2), Ahvenanmaan kysymys (grade 2), Vapaa kielivalinta (grade 2), Jomalan kirkko (grade 1)

**5 worst queries**

1. `far-right.broad`: recall@10 0.17, ndcg@10 0.48; missed Isänmaallinen kansanliike (grade 2, rank 11), Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2, rank 14), Sinimusta liike (grade 2), Isänmaallinen Kansanliike (1993) (grade 1)
2. `sami-prehistory.broad`: recall@10 0.18, ndcg@10 0.29; missed Saamelaiset (grade 2), Suomalaisten alkuperä (grade 2, rank 16), Saamelaisten historia (grade 2, rank 13), Metsäsaamelaiset (grade 2), Lappalaiset (grade 1)
3. `language-politics.broad`: recall@10 0.19, ndcg@10 0.42; missed Suomalainen puolue (grade 2), Suomalaisuuden liitto (grade 2, rank 13), Axel Olof Freudenthal (grade 2), Ahvenanmaan kysymys (grade 2), Vapaa kielivalinta (grade 2)
4. `national-myths.broad`: recall@10 0.20, ndcg@10 0.38; missed Kalevala (grade 2), Elias Lönnrot (grade 2), Lalli (grade 2), Piispa Henrikin surmavirsi (grade 2), Fennomania (grade 1, rank 12)
5. `far-right.en`: recall@10 0.22, ndcg@10 0.58; missed Suomen vastarintaliike (grade 2, rank 19), Suomen Kansa Ensin (grade 2), Kansallissosialismi Suomessa (grade 2, rank 13), Sinimusta liike (grade 2), Suomi – Isänmaa (grade 1)

<details><summary>Per query</summary>

| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |
|---|---|---|---:|---:|---|---:|
| `language-politics.en` | en | topic | 0.38 | 0.62 | no | 15.1 |
| `language-politics.fi` | fi | topic | 0.38 | 0.64 | no | 8.7 |
| `language-politics.broad` | broad | topic | 0.19 | 0.42 | no | 12.4 |
| `language-politics.sv` | sv | topic | 0.31 | 0.59 | no | 7.0 |
| `sami-prehistory.en` | en | topic | 0.29 | 0.53 | no | 5.9 |
| `sami-prehistory.fi` | fi | topic | 0.29 | 0.56 | no | 8.0 |
| `sami-prehistory.broad` | broad | topic | 0.18 | 0.29 | no | 7.6 |
| `sami-prehistory.sv` | sv | topic | 0.35 | 0.68 | no | 8.9 |
| `far-right.en` | en | topic | 0.22 | 0.58 | no | 10.4 |
| `far-right.fi` | fi | topic | 0.26 | 0.69 | no | 11.7 |
| `far-right.broad` | broad | topic | 0.17 | 0.48 | no | 7.8 |
| `far-right.sv` | sv | topic | 0.26 | 0.64 | no | 9.9 |
| `national-myths.en` | en | topic | 0.35 | 0.63 | no | 9.2 |
| `national-myths.fi` | fi | topic | 0.30 | 0.61 | no | 10.3 |
| `national-myths.broad` | broad | topic | 0.20 | 0.38 | no | 9.7 |
| `national-myths.sv` | sv | topic | 0.35 | 0.64 | no | 14.9 |
| `moon-landing-hoax.en` | en | control-out-of-corpus | – | – | no | 5.2 |
| `moon-landing-hoax.fi` | fi | control-out-of-corpus | – | – | no | 6.5 |
| `moon-landing-hoax.broad` | broad | control-out-of-corpus | – | – | no | 5.2 |

</details>

**Failures:** none.

**Cost:** 9.2 s per request (max 15.1 s, 1 at a time), 19 LLM calls, 30,424 in / 9,340 out tokens.

<details><summary>Command</summary>

```
research/eval/run retrieval --split test --name r3-b -- python3 research/24-r3-query/variant.py --mode B
```

</details>
