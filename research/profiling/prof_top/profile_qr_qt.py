#!/usr/bin/env python3
"""Reproducible helper for issue #17 part C (prof_top): Q-R sample & Q-T candidate diffing.

Uses Python 3 stdlib only (`difflib`, `csv`, `random`, `subprocess`, `json`, `re`) and
queries MariaDB via the `mariadb` CLI (`MYSQL_PWD` inherited from environment).

Hard guard: fetches `revision_text` strictly by `(page_id, rev_id)` PK and asserts
total PK rows fetched <= 300 (200 for Q-R + 84 for Q-T = 284 total).
"""

import argparse
import csv
import difflib
import json
import os
import random
import re
import subprocess
from collections import Counter

SEED = 42
SAMPLE_SIZE = 100
MAX_PK_FETCHES = 300

ALLOWED_LABELS = {
    "vandalism/graffiti",
    "test/accidental",
    "good-faith-but-unsourced",
    "narrative/POV-pushing",
    "factual-manipulation",
    "other",
}

# Manual Finnish-diff classifications for the deterministic seed=42 Q-R sample (1..100).
# Tuple format: (expected_title, label, neutral_english_gist)
QR_ANNOTATIONS = {
    1: ("Luettelo suojeluskuntapiireistä", "good-faith-but-unsourced", "Added regional headings and local Civil Guard sub-units without citations."),
    2: ("Carl Gustaf Emil Mannerheim", "factual-manipulation", "Changed presidential term end date (4.3.1946 -> 11.3.1946), regent end date, and removed Defence Council chair role."),
    3: ("Nylon Beat", "factual-manipulation", "Changed breakup year in infobox and added false claim that the pop duo reformed in 2004."),
    4: ("Sininen eduskuntaryhmä", "good-faith-but-unsourced", "Prematurely added MP Arja Juvonen to the new parliamentary group during the June 2017 Finns Party split."),
    5: ("Paavo Väyrynen", "good-faith-but-unsourced", "Updated party end-year to 2017 based on news of planned expulsion before expulsion occurred."),
    6: ("Minna Canth", "vandalism/graffiti", "Inserted explicit sexual graffiti into writer Minna Canth's biography."),
    7: ("Kingston Wall", "good-faith-but-unsourced", "Added unpunctuated, unsourced fan trivia about record prices and the band's drummer."),
    8: ("Jämsänkoski", "test/accidental", "Overwrote external links footer with a chatty school-essay paragraph praising friendly locals and Citymarket."),
    9: ("HX-hävittäjähanke", "other", "Twice-pasted petition spam claiming a fringe domestic fighter bid had PM approval in the HX tender and linking citizens' initiatives."),
    10: ("Martti Ahtisaari", "good-faith-but-unsourced", "Added unsourced pop-culture subsection describing Ahtisaari's caricature in TV satire Itse valtiaat."),
    11: ("Kuunari Koivisto", "other", "Inserted non-political local heritage grievance accusing the City of Pori of shameful destruction for scrapping the schooner Koivisto."),
    12: ("Marja-Liisa Kiljunen", "other", "Replaced general ambassador category with eight non-standard country-specific ambassador categories."),
    13: ("Harvialan metsärata", "other", "Added broad land-transport history category later removed as over-categorization."),
    14: ("Luettelo Suomen historian sodista", "narrative/POV-pushing", "Inserted 2 KB argument classifying Finland's ISAF deployment in Afghanistan as participation in a full-scale US war."),
    15: ("Kunnallisvaalit 1945", "other", "Sourced rewrite on postwar Karelian evacuees reverted administratively due to block evasion (WPK)."),
    16: ("Suomen presidentinvaali 1940", "other", "Infobox and prose restructuring by banned block-evading editor (WPK), reverted administratively."),
    17: ("Suomen tasavallan presidentti", "good-faith-but-unsourced", "Added president-elect Sauli Niinistö to the list of sitting presidents weeks before his inauguration."),
    18: ("Paavo Lipponen", "narrative/POV-pushing", "Inserted tabloid-sourced claim under Nord Stream consultancy that Lipponen humiliated scholar Veli-Pekka Tynkkynen."),
    19: ("Suomettuminen", "narrative/POV-pushing", "Inserted commentary equating Cold War Finlandization self-censorship with PM Vanhanen's Muhammad cartoons stance."),
    20: ("Veikko Vennamo", "other", "Formatting, capitalization, and infobox arrow edits characteristic of block-evading editor (WPK)."),
    21: ("Suomenlahden ulkosaaret", "other", "Large structural reorganization of island lists and lead prose by block-evading editor (WPK)."),
    22: ("Axel Olof Freudenthal", "narrative/POV-pushing", "Juxtaposed RKP's 2004 'spiritual father' tribute with claims Freudenthal called Swedes a master race and Finns inferior Turanians."),
    23: ("Suomen vaalirahoituskohu", "other", "Added summary of a Dec 2007 GRECO report on corruption risks, reverted as off-scope because the report predated the 2008 scandal."),
    24: ("Paavo Lipponen", "vandalism/graffiti", "Inserted crude weight insult claiming Lipponen had a BMI of 50 and faced expulsion from Parliament."),
    25: ("Ville Tikkanen", "good-faith-but-unsourced", "Inserted clumsy, unsourced constituent praise of SDP MP Ville Tikkanen's work on northern roads and war veterans."),
    26: ("Alexander Stubb", "narrative/POV-pushing", "Inserted partisan critique claiming PM Stubb's Twitter habit angered citizens while his government sank the economy."),
    27: ("Martti Lintulahti", "other", "Replaced general ambassador category with non-standard country-specific ambassador categories."),
    28: ("Simon Elo", "good-faith-but-unsourced", "Appended unsourced one-line editorial/quote fragment on Israel/Palestine ('anyone who has visited Israel knows the demand is unrealistic')."),
    29: ("Maria Ohisalo", "vandalism/graffiti", "Defaced Minister Maria Ohisalo's name and called her a clown and Ugandan environment minister."),
    30: ("Helsingin nuorisofestivaalit 1962", "other", "Re-inserted deletion/merge template proposing redirect to the general youth festival article."),
    31: ("Teisko", "good-faith-but-unsourced", "Added three local celebrities as summer-cottage residents without citations."),
    32: ("Käkisalmi", "narrative/POV-pushing", "Changed ceded Karelian town Priozersk/Käkisalmi's infobox country and map from Russia to Finland and founding year 1944 -> 1941."),
    33: ("Juha Rehula", "narrative/POV-pushing", "Inserted partisan anti-Centre Party jab mocking Minister Rehula over municipal democracy."),
    34: ("Antti Nylén", "test/accidental", "Accidentally substituted citation template into raw MediaWiki parser code and self-reverted 28 seconds later."),
    35: ("Venäläisvastaisuus", "narrative/POV-pushing", "Re-inserted nationalist essay link ('Another perspective on Russophobia') during an edit war."),
    36: ("Johannes Virolainen", "good-faith-but-unsourced", "Added unsourced claim about Lohja's voluntary bilingual status in the Swedish-language section."),
    37: ("Riikka Purra", "vandalism/graffiti", "Appended the word 'nazi' into politician Riikka Purra's biography."),
    38: ("Läskikapina", "good-faith-but-unsourced", "Added unsourced folk etymology claiming the 1922 Pork Mutiny was named after US relief bacon crates."),
    39: ("Sanna Marin", "vandalism/graffiti", "Inserted defamatory label and category claiming PM Sanna Marin was mentally disabled, citing a fringe blog."),
    40: ("Suomen presidentinvaali 1943", "other", "Structural and lead rewrite of wartime presidential election article by block-evading editor (WPK)."),
    41: ("Hjallis Harkimo", "test/accidental", "Botched section edit that displaced 135 paragraphs from the top of the biography while adding 51 net bytes."),
    42: ("Turun akatemia", "factual-manipulation", "Inserted fabricated historical claim that 18th-century vandalism destroyed a university building restored in 1901."),
    43: ("Muonio", "vandalism/graffiti", "Inserted nonsensical slang phrases ('vuonna nakki ja pottu', 'asia pihvi?') into municipality article."),
    44: ("V. J. Sukselainen", "other", "Over-linking and school-name tweak reverted administratively as block evasion (WPK)."),
    45: ("Kuopion kouluhyökkäys", "good-faith-but-unsourced", "Appended breaking-news hospital casualty figures without citing a source supporting the numbers."),
    46: ("Ilkka-Christian Björklund", "other", "Pasted verbatim magazine paragraphs on Alpo Rusi's Stasi list, reverted as direct copyright infringement."),
    47: ("Martti Ahtisaari", "vandalism/graffiti", "Inserted juvenile profanity and slurs into President Ahtisaari's infobox."),
    48: ("Nastola", "good-faith-but-unsourced", "Added 2014 urban-settlement population breakdown table without citing Statistics Finland."),
    49: ("Napakymppi (viihdeohjelma)", "good-faith-but-unsourced", "Added unsourced one-line trivia that musician Aki Sirkesalo named Napakymppi his favourite show."),
    50: ("Lestadiolaisuus", "good-faith-but-unsourced", "Added unsourced original-research sentence interpreting Seppo Lohi's remark on the religious contraception ban."),
    51: ("Vienan Karjala", "test/accidental", "Broke Karelian language template syntax and parenthetical Russian orthography in the lead sentence."),
    52: ("Mauri Pekkarinen", "vandalism/graffiti", "Inserted humorous romantic/sexual joke description into politician Mauri Pekkarinen's lead."),
    53: ("Kalevala (korvetti)", "good-faith-but-unsourced", "Added uncited book entry on Finns in 19th-century Japan to the corvette Kalevala's literature list."),
    54: ("Irak-vuoto", "good-faith-but-unsourced", "Added subjective unsourced remark calling Anneli Jäätteenmäki's Iraq-leak quote 'almost legendary'."),
    55: ("Sauli Niinistö", "vandalism/graffiti", "Spammed ten explicit anatomical images and a vulgar pun into President Niinistö's article."),
    56: ("Suvi Auvinen", "vandalism/graffiti", "Inserted juvenile slang insult ('represents sand-people') over ice-hockey celebration criticism under fake summary 'Fixed a typo'."),
    57: ("Uuras", "other", "Modified Russian historical place-name chronology for ceded port Uuras/Vysotsk, reverted for block evasion (Hippophaë)."),
    58: ("Ratsutila", "vandalism/graffiti", "Inserted odd slang joke sentence into historical cavalry-estate article."),
    59: ("Mikael Agricola", "vandalism/graffiti", "Added classroom keyboard-mash graffiti ('xdxdxd') to Mikael Agricola's article."),
    60: ("W.i.t.c.h.", "test/accidental", "Posted all-caps child fan message ('WITCH I LOVE YOU...') directly into article prose."),
    61: ("Urho Kekkonen", "good-faith-but-unsourced", "Added unsourced pop-culture mention of hardcore punk band Kuolleet Kekkoset."),
    62: ("Suomen venäläiset", "factual-manipulation", "Added Finnish-born politician of Ingrian-Finnish parentage (Daniel Sazonov) to list of ethnic Russians in Finland."),
    63: ("Meänkieli", "good-faith-but-unsourced", "Added external link to a youth organization's Meänkieli language booklet PDF."),
    64: ("Anton-tapaus", "other", "Re-inserted misspelled one-sentence subsection noting 7 Päivää tabloid coverage 'from the father's perspective'."),
    65: ("Naarajärven sotavankileiri", "narrative/POV-pushing", "Eight-edit insertion alleging at least 51 Soviet POW shootings, forged medical reports, and a cover-up by Gen. Erik Heinrichs."),
    66: ("Max Jakobson", "vandalism/graffiti", "Inserted explicit pornographic image file into diplomat Max Jakobson's biography."),
    67: ("Jaakko Pukkila", "vandalism/graffiti", "Added nonsense inside-joke claiming kinship to a fictional 'häyhäilijä'."),
    68: ("Jyrki Katainen", "narrative/POV-pushing", "Inserted unsourced partisan claim that Finance Minister Katainen personally caused the 2011 Finns Party surge."),
    69: ("Tyrnävän kotiseutumuseo", "good-faith-but-unsourced", "Added turn-by-turn driving directions to a local heritage museum."),
    70: ("Tony Halme", "vandalism/graffiti", "Inserted juvenile inside-joke ('known as Jeremias's dad') into Tony Halme's biography."),
    71: ("Sauli Niinistö", "factual-manipulation", "Inserted false claim that President Niinistö took up padel, with edit summary explicitly stating 'False information'."),
    72: ("Juha Sipilä", "narrative/POV-pushing", "Inserted multi-paragraph attack accusing PM Sipilä of importing cheap labour, selling national assets, and belonging to a sect."),
    73: ("Suvi Lindén", "narrative/POV-pushing", "Added polemical section claiming Minister Lindén advocated strip-searching employees naked during the Lex Nokia debate."),
    74: ("Mona Bling", "other", "Counter-reverted in BLP naming dispute to re-insert influencer's pre-transition name in the lead and lower follower count."),
    75: ("Tikkakoski", "vandalism/graffiti", "Added personal schoolyard graffiti naming a local individual in all caps."),
    76: ("Urho Kekkonen", "vandalism/graffiti", "Inserted absurd joke claiming President Kekkonen was a pop singer who released a 1934 double-platinum album."),
    77: ("Lyly Rajala", "factual-manipulation", "Inserted false biographical claim that MP Lyly Rajala skipped conscription and held rank 'total objector'."),
    78: ("Martti Ahtisaari", "vandalism/graffiti", "Inserted surrealist nonsense sentences about lizards seizing a sabre."),
    79: ("Savolaiset", "vandalism/graffiti", "Added joke calling PM Jyrki Katainen 'the King of Finland from Siilinjärvi'."),
    80: ("Turun akatemia", "good-faith-but-unsourced", "Added unsourced original-research claim disputing that the Academy of Turku was relocated to Helsinki in 1827."),
    81: ("Jomalan kirkko", "narrative/POV-pushing", "Inserted nationalist linguistic claim that Åland's Jomala church name proves an originally Finnish-speaking population."),
    82: ("Itsenäisyyden Liitto", "other", "Added Hohenlockstedt Jäger monument photo, removed 73 days later as only tangentially related."),
    83: ("Jari Sillanpää", "vandalism/graffiti", "Inserted all-caps homophobic slur graffiti into singer Jari Sillanpää's article."),
    84: ("Suomen esihistoria", "narrative/POV-pushing", "Eight-edit insertion of 2.4 KB fringe theory claiming Paleolithic Europeans (35,000-7000 BC) were genetically and linguistically Finns."),
    85: ("Lapväärtti", "vandalism/graffiti", "Added photo of an M&M's candy dispenser captioned as a 'satellite image of Lapväärtti'."),
    86: ("Björköby", "other", "Changed infobox municipality name format to bilingual 'kunta / kommun' wording."),
    87: ("Isänmaallinen kansanliike", "narrative/POV-pushing", "Inserted categorical claim calling interwar IKL 'a completely fascist organization' whose Blackshirts gave Nazi salutes."),
    88: ("Karoliina Kestin katoamistapaus", "good-faith-but-unsourced", "Added external link to missing teenager's personal Facebook profile."),
    89: ("Anni Sinnemäki", "other", "Added fictional satire-character category to politician Anni Sinnemäki's biography."),
    90: ("Porin Matti", "vandalism/graffiti", "Added joke claiming cast-iron wood stoves are commonly used to heat hot-air balloons."),
    91: ("Tavast (keskiaikainen suku)", "good-faith-but-unsourced", "Added medieval nobleman Arvid Henrikinpoika Tavast without citations and with an inaccurate death date."),
    92: ("Eduskuntavaalit 2015", "good-faith-but-unsourced", "Re-inserted unregistered extra-parliamentary Whisky Party into 2015 election party table."),
    93: ("Talvisota", "good-faith-but-unsourced", "Added unsourced wartime anecdote about roasted crow appearing on a Helsinki restaurant menu."),
    94: ("Suomi-Soffa", "vandalism/graffiti", "Inserted inside-joke hoax naming an individual as 'official sofa tester' awarded an international rest prize."),
    95: ("Homma", "test/accidental", "Copyedit removing forum slang term accidentally duplicated the sentence reporting registered user counts."),
    96: ("Osmo Soininvaara", "narrative/POV-pushing", "Pasted rival party leader Paavo Arhinmäki's blog attack criticizing 'Lex Soininvaara' welfare cuts."),
    97: ("Ulvilan surma", "other", "Eleven-edit insertion of true-crime pro-defence blog arguments challenging forensic evidence in the Ulvila murder case."),
    98: ("Suvi Auvinen", "good-faith-but-unsourced", "Added trivial unsourced claim about promoting Pokémon Go."),
    99: ("Minna Canth", "vandalism/graffiti", "Counter-reverted an anti-vandalism restoration back to a sexually defaced revision during a Minna Canth Day vandalism spree."),
    100: ("Pulkkila", "other", "Re-inserted 1.3 KB promotional text copied verbatim from the municipal web portal."),
}


def run_sql(sql: str) -> list[list[str]]:
    res = subprocess.run(
        ["mariadb", "-h127.0.0.1", "-P3307", "-ucorpus_ro", "--batch", "--raw", "--skip-column-names", "corpus", "-e", sql],
        capture_output=True,
        text=True,
        check=True,
    )
    return [line.split(chr(9)) for line in res.stdout.splitlines() if line]


def split_sentences(para: str) -> list[str]:
    return [p for p in re.split(r"(?<=[.!?])\s+", para.strip()) if p]


def compute_diff_summary(p_txt: str, e_txt: str) -> str:
    p_paras = p_txt.splitlines()
    e_paras = e_txt.splitlines()
    sm = difflib.SequenceMatcher(None, p_paras, e_paras)
    out: list[str] = []
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag == "equal":
            continue
        if tag == "insert":
            for line in e_paras[j1:j2][:15]:
                out.append(f"+PARA: {line[:500]}")
        elif tag == "delete":
            for line in p_paras[i1:i2][:10]:
                out.append(f"-PARA: {line[:300]}")
        elif tag == "replace":
            if (i2 - i1) <= 5 and (j2 - j1) <= 5:
                p_sents = [s for ln in p_paras[i1:i2] for s in (split_sentences(ln) or [ln])]
                e_sents = [s for ln in e_paras[j1:j2] for s in (split_sentences(ln) or [ln])]
                sm2 = difflib.SequenceMatcher(None, p_sents, e_sents)
                for t2, a1, a2, b1, b2 in sm2.get_opcodes():
                    if t2 == "equal":
                        continue
                    for s in p_sents[a1:a2][:8]:
                        out.append(f"  -SENT: {s[:400]}")
                    for s in e_sents[b1:b2][:8]:
                        out.append(f"  +SENT: {s[:400]}")
            else:
                for line in p_paras[i1:i2][:8]:
                    out.append(f"-PARA: {line[:300]}")
                for line in e_paras[j1:j2][:12]:
                    out.append(f"+PARA: {line[:400]}")
    return chr(10).join(out[:40])


def fetch_texts_by_pk(pks: list[tuple[int, int]]) -> dict[tuple[int, int], str]:
    assert len(pks) <= MAX_PK_FETCHES, f"Refusing to fetch {len(pks)} > {MAX_PK_FETCHES} revision_text rows"
    texts: dict[tuple[int, int], str] = {}
    for i in range(0, len(pks), 40):
        chunk = pks[i : i + 40]
        in_list = ", ".join(f"({pid},{rid})" for pid, rid in chunk)
        rows = run_sql(f"SELECT page_id, rev_id, HEX(text) FROM revision_text WHERE (page_id, rev_id) IN ({in_list});")
        for pid_s, rid_s, hex_s in rows:
            texts[(int(pid_s), int(rid_s))] = bytes.fromhex(hex_s).decode("utf-8", errors="replace")
    return texts


def write_labels_csv(records: list[dict], out_csv: str) -> Counter:
    assert len(records) == SAMPLE_SIZE
    assert len(QR_ANNOTATIONS) == SAMPLE_SIZE
    counts: Counter = Counter()
    os.makedirs(os.path.dirname(out_csv), exist_ok=True)
    with open(out_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow([
            "sample_idx",
            "page_id",
            "title",
            "p_rev_id",
            "last_edit_rev_id",
            "r_rev_id",
            "run_len",
            "net_bytes_added",
            "survival_sec",
            "label",
            "gist",
        ])
        for rec in records:
            idx = int(rec["sample_idx"])
            exp_title, label, gist = QR_ANNOTATIONS[idx]
            assert rec["title"] == exp_title, f"Row {idx} title mismatch: {rec['title']} != {exp_title}"
            assert label in ALLOWED_LABELS, f"Invalid label on row {idx}: {label}"
            counts[label] += 1
            writer.writerow([
                idx,
                rec["page_id"],
                rec["title"],
                rec["p_rev_id"],
                rec["last_edit_rev_id"],
                rec["r_rev_id"],
                rec["run_len"],
                rec["net_bytes_added"],
                rec["last_edit_survival_sec"],
                label,
                gist,
            ])
    return counts


def main() -> None:
    parser = argparse.ArgumentParser(description="Export Q-R sample_labels.csv from cached or live PK diffs.")
    parser.add_argument("--from-cache", help="Path to cached qr_sample_42.json (0 DB queries).")
    parser.add_argument(
        "--out-csv",
        default="research/profiling/prof_top/sample_labels.csv",
        help="Output CSV path.",
    )
    args = parser.parse_args()

    if args.from_cache:
        records = json.load(open(args.from_cache, encoding="utf-8"))["records"]
    else:
        rows = run_sql("""
            SELECT page_id, r_rev_id, title, p_rev_id, last_edit_rev_id,
                   run_len, net_bytes_added,
                   TIMESTAMPDIFF(SECOND, last_edit_ts, r_ts) AS last_edit_survival_sec
            FROM research.prof_top_reverts
            WHERE in_scope = 1 AND net_bytes_added >= 50
            ORDER BY page_id, r_rev_id;
        """)
        sampled = random.Random(SEED).sample(rows, SAMPLE_SIZE)
        pks = sorted({(int(r[0]), int(r[3])) for r in sampled} | {(int(r[0]), int(r[4])) for r in sampled})
        _ = fetch_texts_by_pk(pks)
        records = [
            {
                "sample_idx": idx,
                "page_id": int(r[0]),
                "r_rev_id": int(r[1]),
                "title": r[2],
                "p_rev_id": int(r[3]),
                "last_edit_rev_id": int(r[4]),
                "run_len": int(r[5]),
                "net_bytes_added": int(r[6]),
                "last_edit_survival_sec": int(r[7]),
            }
            for idx, r in enumerate(sampled, 1)
        ]

    counts = write_labels_csv(records, args.out_csv)
    for label, cnt in counts.most_common():
        print(f"{label:26s}: {cnt}")


if __name__ == "__main__":
    main()
