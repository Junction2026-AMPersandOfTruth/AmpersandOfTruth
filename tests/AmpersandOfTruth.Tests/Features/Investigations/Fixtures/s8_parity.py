"""Regenerate s8-parity.jsonl: the C# SourceChangeDetector port must reproduce research/35-s8-signals/detect_citation_removal.py
and detect_citation_swap.py.

Cases: every candidate the two Python unit-test modules pass to detect(), plus the wikitext pairs below, diffed by the
real engine (scripts/wikidiff.sh), so the detectors see segments exactly as the app produces them. Each line records the
segments and the Python answers; the C# test replays the segments, so it needs neither Python nor the engine.
Run from the repo root:

    python3 -I tests/AmpersandOfTruth.Tests/Features/Investigations/Fixtures/s8_parity.py research/35-s8-signals \
        > tests/AmpersandOfTruth.Tests/Features/Investigations/Fixtures/s8-parity.jsonl
"""
import json, pathlib, subprocess, sys, unittest

sys.path.insert(0, sys.argv[1])
import detect_citation_removal as removal
import detect_citation_swap as swap

ROOT = pathlib.Path(__file__).resolve().parents[5]
YLE = "<ref>{{Verkkoviite | Osoite = https://yle.fi/a/123 | Nimeke = Arkistolähde | Viitattu = 1.2.2020}}</ref>"
HS = "<ref>{{Verkkoviite | Osoite = https://www.hs.fi/kotimaa/art-1.html | Nimeke = Arkistotutkimus}}</ref>"
LEAD = "Talvisota alkoi 30. marraskuuta 1939, kun Neuvostoliitto hyökkäsi Suomeen."
SECOND = "Sota päättyi Moskovan rauhaan 13. maaliskuuta 1940.<ref>Manninen 1994, s. 28.</ref>"
PAIRS = {
    "pair: citation deleted, sentence stays": (f"{LEAD}{YLE}\n\n{SECOND}", f"{LEAD}\n\n{SECOND}"),
    "pair: sentence rewritten and its named reuse dropped": (
        f'{LEAD}<ref name="yle">Yle 2009.</ref>\n\nTodellisuudessa kyseessä olivat neuvostojoukkojen ampumat laukaukset.<ref name="yle" />',
        f'{LEAD}<ref name="yle">Yle 2009.</ref>\n\nKyseessä saattoi olla neuvostojoukkojen ampumat laukaukset.'),
    "pair: whole paragraph deleted": (f"{LEAD}{YLE}\n\n{SECOND}", f"{LEAD}{YLE}"),
    "pair: whole sentence deleted with its footnote": (
        f"{LEAD} Suomen armeija oli varustettu heikosti mutta puolustautui sitkeästi koko talven.{YLE} Sota kesti 105 päivää.",
        f"{LEAD} Sota kesti 105 päivää."),
    "pair: named reference shortened to a reuse": (
        f'{LEAD}<ref name="YLE">YLE: Suomen tie jatkosotaan, 2010.</ref>\n\nToinen kappale.<ref name="YLE">YLE: Suomen tie jatkosotaan, 2010.</ref>',
        f'{LEAD}<ref name="YLE">YLE: Suomen tie jatkosotaan, 2010.</ref>\n\nToinen kappale.<ref name="YLE" />'),
    "pair: source swapped for another site": (
        f"{LEAD}{HS}", f"{LEAD}<ref>{{{{Verkkoviite | Osoite = https://example.net/juttu-2 | Nimeke = Toinen näkemys}}}}</ref>"),
    "pair: print source swapped, number changed": (
        "Häyhän tiliin on laskettu jopa 542 kaatunutta.<ref>Kansallisbiografia 2003, s. 112.</ref>",
        "Häyhän tiliin on laskettu 259 kaatunutta.<ref>Hurtti-Ukko 1941</ref>"),
    "pair: archive link added to the same source": (
        f"{LEAD}<ref>[http://www.aamulehti.fi/artikkeli/123.html Venäläinen haaveili Karjalan liittämisestä]</ref>",
        f"{LEAD}<ref>{{{{Verkkoviite | Osoite = <!-- http://www.aamulehti.fi/artikkeli/123.html --> | Nimeke = Venäläinen haaveili Karjalan "
        "liittämisestä | arkisto = https://web.archive.org/web/20110406174524/http://www.aamulehti.fi/artikkeli/123.html }}</ref>"),
    "pair: citation moved to the other paragraph": (f"{LEAD}{YLE}\n\nToinen kappale ilman lähdettä.", f"{LEAD}\n\nToinen kappale ilman lähdettä.{YLE}"),
    "pair: section blanked": (
        f"{LEAD}\n\n== Tausta ==\n" + "Pitkä taustakappale, jossa on paljon tekstiä sodan syistä ja neuvotteluista. " * 4 + YLE + "\n\n== Loppu ==\nLoppu.",
        f"{LEAD}\n\n== Tausta ==\n\n== Loppu ==\nLoppu."),
    "pair: one citation dropped, another replaced": (
        f"{LEAD}{YLE} Suomi torjui vaatimukset.{HS}",
        f"{LEAD} Suomi hyväksyi vaatimukset.<ref>Tuntematon blogi 2015</ref>"),
    "pair: citation replaced by a reuse of an existing one": (
        f'{LEAD}<ref name="a">Jakobson 1955, s. 394.</ref>\n\nToinen väite.<ref>Upton 1981, s. 12.</ref>',
        f'{LEAD}<ref name="a">Jakobson 1955, s. 394.</ref>\n\nToinen väite.<ref name="a" />'),
    "pair: closing tag broken": (f"{LEAD}<ref>Haataja 1989.</ref>\n\n== Sodan alku ==\n{SECOND}",
                                 f"{LEAD}<ref>Haataja 1989.myös termi tuli tunnetuksi\n\n== Sodan alku ==\n{SECOND}"),
    "pair: empty citation shell removed": (f"{LEAD}<ref>{{{{Verkkoviite | Osoite =  | Nimeke = | Viitattu = 18.4.2026 }}}}</ref> Loppu.", f"{LEAD} Loppu."),
    "pair: nothing about citations": (f"{LEAD}{YLE}", f"{LEAD}{YLE} Lisätty lause."),
}

cases = []
for module, tests in ((removal, "test_citation_removal"), (swap, "test_citation_swap")):
    original = module.detect
    def spy(cand, _original=original, _tests=tests):
        cases.append((f"{_tests} #{sum(1 for n, _ in cases if n.startswith(_tests)) + 1}", cand))
        return _original(cand)
    module.detect = spy
    unittest.TextTestRunner(stream=open("/dev/null", "w")).run(unittest.defaultTestLoader.loadTestsFromName(tests))
    module.detect = original

requests = "".join(json.dumps({"id": name, "old": old, "new": new}, ensure_ascii=False) + "\n" for name, (old, new) in PAIRS.items())
done = subprocess.run([str(ROOT / "scripts" / "wikidiff.sh"), "batch"], input=requests, capture_output=True, text=True, encoding="utf-8", check=True)
for line in done.stdout.splitlines():
    diff = json.loads(line)
    cases.append((diff["id"], {"old": PAIRS[diff["id"]][0], "diff": diff}))

for name, cand in cases:
    diff = cand["diff"]
    print(json.dumps({
        "name": name,
        "old": cand.get("old") or "",
        "is_blanking": bool(diff["stats"].get("is_blanking")),
        "segments": [{k: s.get(k) for k in ("op", "kind", "text", "prose", "old_paragraph", "new_paragraph", "moved")} for s in diff["segments"]],
        "removed": [{"label": s["label"], "evidence": s["evidence"]} for s in removal.detect(cand)],
        "swapped": [{"label": s["label"], "evidence": s["evidence"]} for s in swap.detect(cand)],
    }, ensure_ascii=False))
