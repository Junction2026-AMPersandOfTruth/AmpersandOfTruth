"""Unit tests for `lexicon.py` (P1.S5, #32)."""

from __future__ import annotations

import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import lexicon  # noqa: E402


class StripSectionTests(unittest.TestCase):
    def test_strips_single_and_repeated_section_headers(self) -> None:
        self.assertEqual(lexicon.strip_section("/* Historia */ lähteetön lisäys"), "lähteetön lisäys")
        self.assertEqual(lexicon.strip_section("/* A */ /* B */ kh"), "kh")
        self.assertEqual(lexicon.strip_section("/* Poliittinen ura */"), "")
        self.assertEqual(lexicon.strip_section(None), "")
        self.assertEqual(lexicon.strip_section(""), "")


class ParseAutoTests(unittest.TestCase):
    def test_family_1_rollback_standard_and_vandalism_and_truncated(self) -> None:
        c1 = (
            "/* Jatkosota */ Käyttäjän [[Special:Contributions/Esimerkki|Esimerkki]] "
            "([[User talk:Esimerkki|keskustelu]]) muokkaukset kumottiin ja sivu palautettiin "
            "viimeisimpään käyttäjän [[User:Toinen|Toinen]] tekemään versioon."
        )
        r1 = lexicon.parse_auto(c1)
        self.assertEqual(
            r1,
            {
                "family": "rollback",
                "target_rev_id": None,
                "reverted_user": "Esimerkki",
                "reason_text": "",
            },
        )

        c2 = (
            "Käyttäjän [[Toiminnot:Muokkaukset/Testaaja|Testaaja]] vandalismi kumottiin ja sivu "
            "palautettiin käyttäjän [[Special:Contributions/Toinen|Toinen]] tekemään versioon 5282992 "
            "(tainut mennä pressat sekaisin?)"
        )
        r2 = lexicon.parse_auto(c2)
        self.assertEqual(
            r2,
            {
                "family": "rollback",
                "target_rev_id": 5282992,
                "reverted_user": "Testaaja",
                "reason_text": "tainut mennä pressat sekaisin?",
            },
        )

        c3 = (
            "Käyttäjän [[Special:Contributions/PitkäNimi|PitkäNimi]] ([[User talk:PitkäNimi|keskustelu]]) "
            "muokkaukset kumottiin ja sivu pal..."
        )
        r3 = lexicon.parse_auto(c3)
        self.assertIsNotNone(r3)
        assert r3 is not None
        self.assertEqual(r3["family"], "rollback")
        self.assertEqual(r3["reverted_user"], "PitkäNimi")
        self.assertEqual(r3["reason_text"], "")

        c4 = (
            "[[Toiminnot:Contributions/FAA|FAA]]:n ([[Keskustelu_käyttäjästä:FAA|keskustelu]]) "
            "muokkaukset kumottiin ja artikkeli palautettiin viimeisimpään ML:n tekemään versioon."
        )
        self.assertEqual(
            lexicon.parse_auto(c4),
            {
                "family": "rollback",
                "target_rev_id": None,
                "reverted_user": "FAA",
                "reason_text": "",
            },
        )

    def test_family_2_undo_modern_with_diff_link_and_tails(self) -> None:
        c1 = (
            "/* Alku */ Kumottu muokkaus 15646527, jonka teki "
            "[[Special:Contributions/Muokkaaja A|Muokkaaja A]] "
            "([[User talk:Muokkaaja A|keskustelu]]); tuohan se vasta propagandaa olikin"
        )
        r1 = lexicon.parse_auto(c1)
        self.assertEqual(
            r1,
            {
                "family": "undo_modern",
                "target_rev_id": 15646527,
                "reverted_user": "Muokkaaja A",
                "reason_text": "tuohan se vasta propagandaa olikin",
            },
        )

        c2 = (
            "Kumottu muokkaus [[Special:Diff/22166886|22166886]], jonka teki "
            "[[Special:Contributions/192.0.2.1|192.0.2.1]] ([[User talk:192.0.2.1|keskustelu]]) "
            "(MobileUndo v2.0.0)"
        )
        r2 = lexicon.parse_auto(c2)
        self.assertEqual(
            r2,
            {
                "family": "undo_modern",
                "target_rev_id": 22166886,
                "reverted_user": "192.0.2.1",
                "reason_text": "",
            },
        )

    def test_family_3_flaggedrevs_variants_and_ipv6_restored_author(self) -> None:
        c1 = (
            "Hylättiin viimeisin tekstimuutos (tehnyt [[Toiminnot:Muokkaukset/Muokkaaja1|Muokkaaja1]]) "
            "ja palautettiin versio 14936880, jonka on tehnyt Tarkastaja: rv, lähteetön lisäys"
        )
        r1 = lexicon.parse_auto(c1)
        self.assertEqual(
            r1,
            {
                "family": "flaggedrevs",
                "target_rev_id": 14936880,
                "reverted_user": "Muokkaaja1",
                "reason_text": "rv, lähteetön lisäys",
            },
        )

        # Multi-word restored author without tail must not spill second name token into reason_text.
        c2 = (
            "Hylättiin viimeisin tekstimuutos sotkemisena (tehnyt [[Toiminnot:Muokkaukset/X|X]]) "
            "ja palautettiin versio 22197627, jonka on tehnyt Ville Siliämaa"
        )
        r2 = lexicon.parse_auto(c2)
        self.assertEqual(
            r2,
            {
                "family": "flaggedrevs",
                "target_rev_id": 22197627,
                "reverted_user": "X",
                "reason_text": "",
            },
        )

        # IPv6 restored author with colons inside address + trailing reason.
        c3 = (
            "Hylättiin viimeisimmät 2 tekstimuutosta (tehnyt [[Toiminnot:Muokkaukset/Y|Y]]) "
            "ja palautettiin versio 22925301, jonka on tehnyt 2001:db8:85a3::8a2e:370:7334 Lähteetöntä"
        )
        r3 = lexicon.parse_auto(c3)
        self.assertEqual(
            r3,
            {
                "family": "flaggedrevs",
                "target_rev_id": 22925301,
                "reverted_user": "Y",
                "reason_text": "Lähteetöntä",
            },
        )

        # Partial reject of earliest pending revision (", joka tehtiin versioon <id>").
        c4 = (
            "Hylättiin ensimmäinen tekstimuutos (tehnyt [[Toiminnot:Muokkaukset/Z|Z]]), "
            "joka tehtiin versioon 23324888, jonka on tehnyt Tarkastaja: virheellinen väite"
        )
        self.assertEqual(
            lexicon.parse_auto(c4),
            {
                "family": "flaggedrevs",
                "target_rev_id": 23324888,
                "reverted_user": "Z",
                "reason_text": "virheellinen väite",
            },
        )

    def test_family_4_undo_legacy(self) -> None:
        c1 = (
            "Käyttäjän [[Toiminnot:Contributions/ Esim |Esim]] "
            "([[Keskustelu_käyttäjästä:Esim|keskustelu]]) muokkaus 4829719 kumottiin; lähteetön näkemys"
        )
        r1 = lexicon.parse_auto(c1)
        self.assertEqual(
            r1,
            {
                "family": "undo_legacy",
                "target_rev_id": 4829719,
                "reverted_user": "Esim",
                "reason_text": "lähteetön näkemys",
            },
        )

        c2 = "Käyttäjän 192.0.2.55 muokkaukset kumottiin: sotkua"
        r2 = lexicon.parse_auto(c2)
        self.assertEqual(
            r2,
            {
                "family": "undo_legacy",
                "target_rev_id": None,
                "reverted_user": "192.0.2.55",
                "reason_text": "sotkua",
            },
        )

    def test_family_5_popups_and_en_gadgets(self) -> None:
        c1 = (
            "Revert to revision 2442947 dated 2007-03-02 12:30:00 by Tarkastaja "
            "using [[:en:Wikipedia:Tools/Navigation_popups|popups]]"
        )
        r1 = lexicon.parse_auto(c1)
        self.assertEqual(
            r1,
            {
                "family": "popups_en",
                "target_rev_id": 2442947,
                "reverted_user": None,
                "reason_text": "",
            },
        )

        c2 = "Undid revision 20273889 by [[Special:Contributions/Testi|Testi]]; vandalism ([[:meta:User:Patrol|Patrol]])"
        r2 = lexicon.parse_auto(c2)
        self.assertEqual(
            r2,
            {
                "family": "popups_en",
                "target_rev_id": 20273889,
                "reverted_user": "Testi",
                "reason_text": "vandalism",
            },
        )

        c3 = "Reverted 2 edits by [[Special:Contributions/Testi|Testi]] ([[User talk:Testi|talk]]): spam (TwinkleGlobal)"
        r3 = lexicon.parse_auto(c3)
        self.assertEqual(
            r3,
            {
                "family": "popups_en",
                "target_rev_id": None,
                "reverted_user": "Testi",
                "reason_text": "spam",
            },
        )

    def test_family_6_legacy_rollback(self) -> None:
        c1 = "Palautettiin Esim:n muokkaukset viimeiseen Toinen:n tekemään muutokseen."
        r1 = lexicon.parse_auto(c1)
        self.assertEqual(
            r1,
            {
                "family": "legacy_rollback",
                "target_rev_id": None,
                "reverted_user": "Esim",
                "reason_text": "",
            },
        )

        c2 = "Palautettu Tarkastajan viimeisimpään muutokseen: vuosiluvun ja viivan väliin ei tule välilyöntiä"
        r2 = lexicon.parse_auto(c2)
        self.assertEqual(
            r2,
            {
                "family": "legacy_rollback",
                "target_rev_id": None,
                "reverted_user": None,
                "reason_text": "vuosiluvun ja viivan väliin ei tule välilyöntiä",
            },
        )

    def test_non_auto_returns_none(self) -> None:
        self.assertIsNone(lexicon.parse_auto("/* Lähteet */ lisätty viite"))
        self.assertIsNone(lexicon.parse_auto("rv, lähteetön väite"))
        self.assertIsNone(lexicon.parse_auto(""))
        self.assertIsNone(lexicon.parse_auto(None))

    def test_username_inside_template_does_not_leak_into_reason_text(self) -> None:
        c = "Kumottu muokkaus 15037779, jonka teki [[Special:Contributions/Propaganda|Propaganda]] ([[User talk:Propaganda|keskustelu]])"
        parsed = lexicon.parse_auto(c)
        self.assertIsNotNone(parsed)
        assert parsed is not None
        self.assertEqual(parsed["reason_text"], "")
        self.assertEqual(lexicon.reasons(lexicon.extract_reason_text(c)), set())


class ReasonsLexiconTests(unittest.TestCase):
    def test_every_category_matches_inflected_finnish_and_english(self) -> None:
        self.assertIn("vandalism_test", lexicon.reasons("sotkemista ja häiriköintiä"))
        self.assertIn("vandalism_test", lexicon.reasons("rvv"))
        self.assertIn("vandalism_test", lexicon.reasons("vandalism"))

        self.assertIn("sourcing", lexicon.reasons("lähteettömiä väitteitä"))
        self.assertIn("sourcing", lexicon.reasons("ei tietolähdettä"))
        self.assertIn("sourcing", lexicon.reasons("lähde?"))
        self.assertIn("sourcing", lexicon.reasons("unsourced"))

        self.assertIn("neutrality_pov", lexicon.reasons("POV-pushausta"))
        self.assertIn("neutrality_pov", lexicon.reasons("NPOV"))
        self.assertIn("neutrality_pov", lexicon.reasons("poliittista propagandaa"))
        self.assertIn("neutrality_pov", lexicon.reasons("puolueellista ja epäneutraalia spekulaatiota"))
        self.assertIn("neutrality_pov", lexicon.reasons("kirjoittajan oma mielipidekirjoitus"))

        self.assertIn("factual_error", lexicon.reasons("väärää tietoa ja asiavirheitä"))
        self.assertIn("factual_error", lexicon.reasons("valheellinen väite, ei pidä paikkaansa"))

        self.assertIn("original_research", lexicon.reasons("ei uutta tutkimusta eikä omaa tutkimusta"))
        self.assertIn("original_research", lexicon.reasons("WP:OR"))

        self.assertIn("block_evasion", lexicon.reasons("rv, eston kiertoa"))
        self.assertIn("block_evasion", lexicon.reasons("estonkiertoa"))

        self.assertIn("copyright", lexicon.reasons("tekijänoikeusrikkomus, suoraa kopiota"))
        self.assertIn("copyright", lexicon.reasons("copyvio, kopioitu suoraan"))

        self.assertIn("maintenance", lexicon.reasons("kh, typo ja kielilinkit"))
        self.assertIn("maintenance", lexicon.reasons("kielenhuoltoa, luokat ja format"))

        self.assertIn("consensus", lexicon.reasons("ei konsensusta, ks. keskustelusivulla"))
        self.assertIn("consensus", lexicon.reasons("per keskustelu"))

    def test_false_positive_guards_popov_sotkamo_vaara_and_section_headers(self) -> None:
        # Word boundaries: Popov / Karpov must not match POV; Sotkamo / sotka must not match sotku*.
        self.assertNotIn("neutrality_pov", lexicon.reasons("Aleksandr Popov ja Anatoli Karpov"))
        self.assertNotIn("vandalism_test", lexicon.reasons("Sotkamon kunta ja Sotkamo-seura, sotka"))

        # Accent sensitivity: vaara ("danger" / "hill") != väärä ("wrong").
        self.assertNotIn("factual_error", lexicon.reasons("Kolin vaara ja suuri vaaratilanne"))
        self.assertIn("factual_error", lexicon.reasons("väärä vuosiluku"))

        # Plain "rv" is generic revert shorthand, not a reason category on its own.
        self.assertEqual(lexicon.reasons("rv"), set())
        self.assertEqual(lexicon.reasons("rv, lähteetön"), {"sourcing"})

        # Spelling compound "kirjoitusvirhe" is maintenance, not factual_error.
        self.assertEqual(lexicon.reasons("kirjoitusvirhe korjattu"), {"maintenance"})

        # Section headers must be stripped before matching.
        self.assertEqual(lexicon.reasons("/* Propaganda ja mielipiteet */"), set())
        self.assertEqual(lexicon.reasons("/* Lähteetön spekulaatio */ kh"), {"maintenance"})


class InsertFlagsTests(unittest.TestCase):
    def test_empty_large_and_section_only_large(self) -> None:
        self.assertEqual(lexicon.insert_flags("", 500, False), {"empty_large"})
        self.assertEqual(lexicon.insert_flags("/* Historia */", -750, False), {"empty_large"})
        self.assertEqual(lexicon.insert_flags("", 499, False), set())

    def test_minor_label_large(self) -> None:
        self.assertEqual(lexicon.insert_flags("typo", 620, False), {"minor_label_large"})
        self.assertEqual(lexicon.insert_flags("/* Urheilu */ kh", 800, False), {"minor_label_large"})
        self.assertEqual(lexicon.insert_flags("pieni korjaus", 510, False), {"minor_label_large"})
        self.assertEqual(lexicon.insert_flags("laajennettu tekstiä", 900, True), {"minor_label_large"})
        self.assertEqual(lexicon.insert_flags("typo", 120, False), set())

    def test_argues_position(self) -> None:
        self.assertIn("argues_position", lexicon.insert_flags("Totuus sisällissodasta", 200, False))
        self.assertIn("argues_position", lexicon.insert_flags("Mitä oikeasti tapahtui", 150, False))
        self.assertIn("argues_position", lexicon.insert_flags("Todellisuudessa asia oli toisin", 150, False))
        self.assertIn("argues_position", lexicon.insert_flags("Korjattu propagandaa!", 150, False))
        # HTML comment bang must not falsely trigger argues_position.
        self.assertNotIn("argues_position", lexicon.insert_flags("lisätty <!-- huom --> teksti", 50, False))

    def test_cites_source_and_not_unsourced(self) -> None:
        self.assertIn("cites_source", lexicon.insert_flags("lisätty lähde ja ref", 250, False))
        self.assertIn("cites_source", lexicon.insert_flags("https://yle.fi/a/123", 120, False))
        # "lähteetön" / "ei lähdettä" / "lähde?" must NOT trigger cites_source.
        self.assertNotIn("cites_source", lexicon.insert_flags("poistettu lähteetön väite", -200, False))
        self.assertNotIn("cites_source", lexicon.insert_flags("ei lähdettä", -100, False))
        self.assertNotIn("cites_source", lexicon.insert_flags("lähde?", -50, False))

    def test_auto_revert_does_not_receive_insert_flags(self) -> None:
        rb = (
            "Käyttäjän [[Special:Contributions/A|A]] muokkaukset kumottiin ja sivu palautettiin "
            "viimeisimpään käyttäjän [[User:B|B]] tekemään versioon."
        )
        self.assertEqual(lexicon.insert_flags(rb, 1500, True), set())


if __name__ == "__main__":
    unittest.main()
