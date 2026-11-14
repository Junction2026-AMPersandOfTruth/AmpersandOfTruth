# /// script
# dependencies = ["pymysql"]
# ///
"""Before blind inputs go to a third-party model (Gemini): replace every corpus account name in edit summaries with
"[editor]" (evidence-standard §3: model prompts never contain usernames or IPs). Summaries only: diffs are article text.

    research/20-golden/golden.sh scrub_inputs.py labels/*.blind-input.json
"""
import json
import re
import sys
from pathlib import Path

from q import connect

with connect() as c, c.cursor() as cur:
    cur.execute("SELECT DISTINCT `user` FROM revision WHERE anonymous = 0 AND `user` IS NOT NULL")
    names = {r[0] for r in cur.fetchall() if r[0] and len(r[0]) >= 2}
TOKEN = re.compile(r"[^\s\[\](){}|,.:;'\"!?]+")
IP = re.compile(r"(?<![\d.])\d{1,3}(?:\.\d{1,3}){3}(?![\d.])")


def scrub(s: str) -> tuple[str, int]:
    n = 0

    def rep(m):
        nonlocal n
        w = m.group(0)
        if (w in names or w[:1].upper() + w[1:] in names) and not re.fullmatch(r"E\d+", w):
            n += 1
            return "[editor]"
        return w
    s = TOKEN.sub(rep, s)
    s, k = IP.subn("[ip]", s)
    return s, n + k


for path in sys.argv[1:]:
    p = Path(path)
    d = json.loads(p.read_text())
    total = 0
    for it in d["items"]:
        for c in it["candidates"]:
            c["removal_summary"], k = scrub(c.get("removal_summary") or ""); total += k
            for e in c["edits"]:
                e["summary"], k = scrub(e.get("summary") or ""); total += k
    p.write_text(json.dumps(d, ensure_ascii=False, indent=1))
    print(f"{p.name}: {total} name(s)/IP(s) replaced in summaries")
