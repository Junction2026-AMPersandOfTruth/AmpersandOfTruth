#!/usr/bin/env python3
"""Offline unit tests for P1.S7 (#34) claim clustering logic (no database access)."""
from __future__ import annotations

import pathlib
import sys
import unittest

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import cluster  # noqa: E402
import variant  # noqa: E402


def _make_unit(
    reverting_rev_id: int,
    text: str,
    *,
    page_id: int = 100,
    inserting_rev_ids: list[int] | None = None,
    inserted_at: str = "2020-01-01 12:00:00",
    removed_at: str = "2020-01-01 13:00:00",
    user: str = "EditorOne",
) -> cluster.Unit:
    parts = [text]
    norm = cluster.normalise_text(text)
    return cluster.Unit(
        page_id=page_id,
        reverting_rev_id=reverting_rev_id,
        inserting_rev_ids=inserting_rev_ids or [reverting_rev_id - 1],
        inserted_at=inserted_at,
        removed_at=removed_at,
        account_key=cluster.make_account_key(user),
        prose_chars=len(norm),
        sample_text=cluster.make_sample_text(parts, 300),
        sentences=cluster.extract_sentences(parts),
        norm_text=norm,
    )


class S7ClusteringTests(unittest.TestCase):
    def test_extract_unit_parts_respects_segment_kinds_and_moves(self) -> None:
        segments = [
            {"op": "added", "kind": "prose", "prose": "Lisätty tekstikappale historiasta.", "text": "'''Lisätty''' tekstikappale historiasta.", "moved": False},
            {"op": "added", "kind": "prose", "prose": "Siirretty vanha kappale muuttumattomana.", "text": "Siirretty vanha kappale muuttumattomana.", "moved": True},
            {"op": "removed", "kind": "prose", "prose": "Poistettu vanha teksti kokonaan.", "text": "Poistettu vanha teksti kokonaan.", "moved": False},
            {"op": "added", "kind": "template", "prose": "Infobox-arvo 1918", "text": "{{Malline|Infobox-arvo 1918}}", "moved": False},
            {"op": "added", "kind": "reference", "prose": "Lähde 2020", "text": "<ref>https://example.org/lahde Lähde 2020</ref>", "moved": False},
            {"op": "added", "kind": "category", "prose": "", "text": "[[Luokka:Suomen historia]]", "moved": False},
        ]
        parts = cluster.extract_unit_parts(segments)
        self.assertEqual(
            parts,
            [
                "Lisätty tekstikappale historiasta.",
                "Infobox-arvo 1918",
                "<ref>https://example.org/lahde Lähde 2020</ref>",
            ],
        )

    def test_normalisation_strips_markup_and_punctuation_preserving_finnish(self) -> None:
        raw = "'''Suomi''' liittyi [[Euroopan unioni|Euroopan unioniin]] vuonna 1995!<ref name='x'>Lähde</ref>"
        norm = cluster.normalise_text(raw)
        self.assertEqual(norm, "suomi liittyi euroopan unioniin vuonna 1995 lähde")

        short = "Lyhyt lause. Toinen pitkä lause, jossa on selvästi yli kolmekymmentä merkkiä tekstiä!"
        sents = cluster.extract_sentences([short])
        self.assertEqual(len(sents), 1)
        self.assertIn("toinen pitkä lause", sents[0])
        self.assertGreaterEqual(len(sents[0]), 30)

    def test_minhash_lsh_finds_reworded_pair_and_rejects_unrelated(self) -> None:
        claim_a = cluster.normalise_text(
            "Suomi osallistui Afganistanin sodan sotatoimiin Naton alaisissa ISAF joukoissa vuodesta 2002 lähtien aktiivisesti."
        )
        # Reworded: changed two words so exact sentence hash differs, while 5-shingle Jaccard > 0.6
        claim_b = cluster.normalise_text(
            "Suomi osallistui Afganistanin sodan sotatoimiin Naton alaisissa ISAF joukoissa vuodesta 2002 alkaen pysyvästi."
        )
        unrelated = cluster.normalise_text(
            "Talvisodan rauhansopimus allekirjoitettiin Moskovassa maaliskuussa 1940 pitkien neuvottelujen jälkeen."
        )
        self.assertNotEqual(cluster.sentence_hash(claim_a), cluster.sentence_hash(claim_b))

        shingles = [
            cluster.word_shingles(claim_a),
            cluster.word_shingles(claim_b),
            cluster.word_shingles(unrelated),
        ]
        true_sim = cluster.jaccard(shingles[0], shingles[1])
        self.assertGreaterEqual(true_sim, 0.6)
        self.assertEqual(cluster.jaccard(shingles[0], shingles[2]), 0.0)

        lsh = cluster.MinHashLSH()
        links, _cand_count = lsh.find_fuzzy_links(shingles, threshold=0.6)
        linked_pairs = {(u, v) for u, v, _ in links}
        self.assertIn((0, 1), linked_pairs)
        self.assertNotIn((0, 2), linked_pairs)
        self.assertNotIn((1, 2), linked_pairs)

    def test_union_find_connected_components(self) -> None:
        uf = cluster.UnionFind(5)
        self.assertTrue(uf.union(0, 2))
        self.assertTrue(uf.union(2, 4))
        self.assertFalse(uf.union(0, 4))
        self.assertEqual(uf.components(), [[0, 2, 4], [1], [3]])

    def test_boilerplate_guard_blocks_hashes_over_eight_pages(self) -> None:
        boilerplate = "Tämä täsmälleen sama mallinelause toistuu yli kahdeksalla eri artikkelisivulla."
        real_claim = "Kenraali Mannerheim toimi valtionhoitajana vuoden 1919 hallitusmuodon vahvistamiseen asti."

        units = [
            # Same boilerplate sentence on 9 distinct pages -> must NOT link
            _make_unit(100 + i * 2, boilerplate, page_id=1000 + i, user=f"User{i}")
            for i in range(9)
        ] + [
            # Real claim on 2 pages -> MUST link
            _make_unit(500, real_claim, page_id=2001, user="Alice"),
            _make_unit(502, real_claim, page_id=2002, user="Bob"),
        ]

        exact_clusters, fuzzy_clusters, stats = cluster.cluster_units(units)
        self.assertEqual(stats["boilerplate_hashes_blocked"], 1)
        # 9 unlinked boilerplate singletons + 1 linked 2-unit cluster = 10 clusters in both exact and fuzzy
        self.assertEqual(len(exact_clusters), 10)
        self.assertEqual(len(fuzzy_clusters), 10)
        multi = [c for c in fuzzy_clusters if c.n_units >= 2]
        self.assertEqual(len(multi), 1)
        self.assertEqual(multi[0].n_units, 2)
        self.assertEqual(multi[0].n_pages, 2)

    def test_account_key_masks_ip_ranges_and_hides_raw_names(self) -> None:
        k_ip_a = cluster.make_account_key("85.76.12.34")
        k_ip_b = cluster.make_account_key("85.76.250.9")
        k_ip_other = cluster.make_account_key("85.77.12.34")
        k_user = cluster.make_account_key("MattiMeikäläinen")
        self.assertEqual(k_ip_a, k_ip_b)  # same /16 IPv4 counts as one editor
        self.assertNotEqual(k_ip_a, k_ip_other)
        self.assertNotIn("Matti", k_user)
        self.assertNotIn("85.76", k_ip_a)
        self.assertEqual(len(k_user), 12)

        sample = cluster.make_sample_text(["Muokkaus osoitteesta 85.76.12.34 tekstissä."])
        self.assertNotIn("85.76.12.34", sample)
        self.assertIn("85.76.x.x", sample)

    def test_campaign_signals_and_sleeper_gap(self) -> None:
        claim = "Suomen hallitus päätti salaisesta sopimuksesta ilman eduskunnan käsittelyä syksyllä."
        units = [
            _make_unit(
                10,
                claim,
                page_id=500,
                inserted_at="2021-01-01 10:00:00",
                removed_at="2021-01-01 12:00:00",
                user="85.76.1.1",
            ),
            _make_unit(
                20,
                claim,
                page_id=500,
                inserted_at="2021-01-02 10:00:00",
                removed_at="2021-01-02 11:00:00",
                user="85.76.99.2",  # same /16 -> still 1 account so far
            ),
            _make_unit(
                30,
                claim,
                page_id=501,
                inserted_at="2021-02-01 11:00:00",
                removed_at="2021-02-01 14:00:00",
                user="SecondNamedEditor",
            ),
        ]
        exact_clusters, _, _ = cluster.cluster_units(units)
        self.assertEqual(len(exact_clusters), 1)
        c = exact_clusters[0]
        self.assertEqual(c.n_units, 3)
        self.assertEqual(c.n_reinsertions, 2)
        self.assertEqual(c.n_pages, 2)
        self.assertEqual(c.n_accounts, 2)
        self.assertGreaterEqual(c.span_days, 31.0)
        self.assertAlmostEqual(c.max_gap_days, 30.0, places=2)
        self.assertEqual(c.campaign_signals, "accounts,articles,persistence")

        # Below persistence thresholds: 2 units over 10 days (needs >=3 insertions), and 3 units over 2 days (needs >=7d)
        self.assertEqual(cluster.compute_campaign_signals(2, 1, 1, 10.0), [])
        self.assertEqual(cluster.compute_campaign_signals(3, 1, 1, 2.0), [])
        self.assertEqual(cluster.compute_campaign_signals(3, 1, 1, 7.0), ["persistence"])

    def test_variant_incident_builder_and_ranking(self) -> None:
        meta = {
            "cluster_id": "42",
            "n_units": "2",
            "n_pages": "2",
            "n_accounts": "2",
            "n_reinsertions": "1",
            "span_days": "3.5",
            "prose_chars": "400",
            "campaign_signals": "accounts,articles",
        }
        members = [
            {
                "page_id": "2002",
                "reverting_rev_id": "105",
                "inserting_rev_ids": "[103,104]",
                "inserted_at": "2022-05-02 10:00:00",
                "removed_at": "2022-05-02 12:00:00",
            },
            {
                "page_id": "2001",
                "reverting_rev_id": "103",  # overlaps with next span's inserting rev 103 -> must be stripped from removing
                "inserting_rev_ids": "[101]",
                "inserted_at": "2022-05-01 08:00:00",
                "removed_at": "2022-05-01 09:00:00",
            },
        ]
        built = variant.build_incident_from_cluster(meta, members)
        self.assertIsNotNone(built)
        sc, prose_c, max_rem, inc = built
        self.assertEqual(inc["page_id"], 2001)
        self.assertEqual(inc["other_page_ids"], [2002])
        self.assertEqual(inc["inserting_rev_ids"], [101, 103, 104])
        self.assertEqual(inc["removing_rev_ids"], [105])
        self.assertEqual(inc["tier"], "reverted")
        self.assertEqual(prose_c, 400)
        self.assertEqual(max_rem, 105)
        self.assertGreater(sc, variant.score_cluster(1, 1, 1, 0.0, 400))


if __name__ == "__main__":
    unittest.main()
