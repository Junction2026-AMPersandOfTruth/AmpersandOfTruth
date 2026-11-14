"""Regenerate s5-parity.jsonl: the C# RevertReasons port must reproduce research/32-s5-summaries/lexicon.py.

Records every input the Python unit tests pass to parse_auto/reasons (+ a few revert summaries) with the
Python outputs. Run from the repo root:  python3 -I tests/AmpersandOfTruth.Tests/Features/Investigations/Fixtures/s5_parity.py \
    research/32-s5-summaries > tests/AmpersandOfTruth.Tests/Features/Investigations/Fixtures/s5-parity.jsonl
"""
import json, sys, unittest
sys.path.insert(0, sys.argv[1])
import lexicon
seen = []
orig_parse, orig_reasons = lexicon.parse_auto, lexicon.reasons
def parse_auto(c): seen.append(c); return orig_parse(c)
def reasons(t): seen.append(t); return orig_reasons(t)
lexicon.parse_auto, lexicon.reasons = parse_auto, reasons
import test_lexicon
unittest.TextTestRunner(stream=open('/dev/null','w')).run(unittest.defaultTestLoader.loadTestsFromModule(test_lexicon))
lexicon.parse_auto, lexicon.reasons = orig_parse, orig_reasons
extra = ["Hylättiin viimeisin tekstimuutos (tehnyt 192.0.2.1) ja palautettiin versio 123 käyttäjältä Matti: väärää tietoa",
         "Kumottu muokkaus 23181977, jonka teki [[Special:Contributions/85.76.1.2|85.76.1.2]] ([[User talk:85.76.1.2|keskustelu]]) propagandaa",
         "rv, ei pidä paikkaansa", "lähteetön ja puolueellinen", "palautettu, ks. keskustelusivu"]
out = []
for c in dict.fromkeys([s for s in seen if isinstance(s, str)] + extra):
    out.append({"comment": c, "reason_text": lexicon.extract_reason_text(c), "reasons": sorted(lexicon.reasons(lexicon.extract_reason_text(c))),
                "raw_reasons": sorted(lexicon.reasons(c))})
for o in out: print(json.dumps(o, ensure_ascii=False))
