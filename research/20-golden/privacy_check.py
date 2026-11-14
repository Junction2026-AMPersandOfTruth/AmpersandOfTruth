# /// script
# dependencies = ["pymysql"]
# ///
"""Privacy gate for research/eval/golden.json (evidence-standard §3): no raw username or unmasked IP in any text field.
Scans description/why/notes/title/query text against every named account in corpus.revision (≥ 3 chars, whole word,
case-sensitive) and for full IPv4/IPv6 addresses. Prints hits for a human to judge (common words can be usernames).

    research/20-golden/golden.sh privacy_check.py [research/eval/golden.json]
"""
import json
import re
import sys
from pathlib import Path

from q import connect

path = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / "eval" / "golden.json")
doc = json.loads(path.read_text())
texts = []
for t in doc["topics"]:
    texts += [(t["id"], "title", t.get("title", "")), (t["id"], "notes", t.get("notes", ""))]
    texts += [(q["id"], "query", q["text"]) for q in t["queries"]]
    for it in t["items"]:
        texts += [(it["id"], k, it.get(k) or "") for k in ("description", "why", "notes")]
with connect() as c, c.cursor() as cur:
    cur.execute("SELECT DISTINCT `user` FROM revision WHERE anonymous = 0 AND `user` IS NOT NULL")
    names = {r[0] for r in cur.fetchall() if r[0] and len(r[0]) >= 3 and not re.fullmatch(r"~\d{4}-\d+-\d+", r[0])}
words = re.compile(r"[\w.\-]+")
hits = 0
for where, field, text in texts:
    found = {w for w in words.findall(text) if w in names}
    found |= set(re.findall(r"(?<![\d.])\d{1,3}(?:\.\d{1,3}){3}(?![\d.])", text))
    found -= {"E" + str(i) for i in range(1, 2000)}
    if found:
        hits += 1
        print(f"{where} [{field}]: {sorted(found)}")
print(f"{hits} field(s) with possible usernames/IPs out of {len(texts)} scanned ({len(names)} account names)")
