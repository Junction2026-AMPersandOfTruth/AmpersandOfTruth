# /// script
# dependencies = ["pymysql"]
# ///
"""Ad-hoc read-only corpus query: `uv run research/20-golden/q.py "SELECT ..."` → TSV on stdout.
Corpus access from env (CONTRACT.md §7): CORPUS_HOST/PORT/USER/PW/DB. See golden.sh for the wrapper that sets CORPUS_PW."""
import os
import sys

import pymysql


def connect():
    return pymysql.connect(
        host=os.environ.get("CORPUS_HOST", "127.0.0.1"),
        port=int(os.environ.get("CORPUS_PORT", "3307")),
        user=os.environ.get("CORPUS_USER", "corpus_ro"),
        password=os.environ["CORPUS_PW"],
        database=os.environ.get("CORPUS_DB", "corpus"),
        charset="utf8mb4",
        read_timeout=600,
    )


if __name__ == "__main__":
    with connect() as c, c.cursor() as cur:
        cur.execute(sys.argv[1])
        if cur.description:
            print("\t".join(d[0] for d in cur.description))
            for row in cur.fetchall():
                print("\t".join("" if v is None else str(v) for v in row))
