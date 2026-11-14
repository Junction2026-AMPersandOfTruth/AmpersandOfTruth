# /// script
# dependencies = ["pymysql"]
# ///
"""P0.7: how do editors appear in corpus.revision? Feeds the editor-display rule.
Run: scripts/corpus-tunnel.sh && CORPUS_PW=... uv run research/p0.7-evidence-standard/editor_profile.py"""
import os, sys, pymysql
c = pymysql.connect(host="127.0.0.1", port=3307, user="corpus_ro", password=os.environ["CORPUS_PW"], database="corpus")
cur = c.cursor()
def q(label, sql):
    cur.execute(sql); rows = cur.fetchall()
    print(f"\n## {label}\n{sql.strip()}")
    for r in rows: print("  ", r)
if len(sys.argv) > 1 and sys.argv[1] == "schema":
    q("tables", "SHOW TABLES")
    q("revision columns", "SHOW COLUMNS FROM revision")
    sys.exit()
q("editor kinds", """
SELECT CASE
  WHEN user IS NULL OR user = '' THEN 'hidden/empty'
  WHEN user LIKE '~%' THEN 'temporary account'
  WHEN user REGEXP '^[0-9]{1,3}(\\\\.[0-9]{1,3}){3}$' THEN 'ipv4'
  WHEN user REGEXP '^[0-9A-Fa-f:]+$' AND user LIKE '%:%' THEN 'ipv6'
  WHEN user REGEXP '(?i)bot$' THEN 'bot-like name'
  ELSE 'named' END AS kind,
  anonymous, COUNT(*) n, COUNT(DISTINCT user) editors, MIN(ts), MAX(ts)
FROM revision GROUP BY kind, anonymous ORDER BY n DESC""")
q("sample temp accounts", "SELECT DISTINCT user FROM revision WHERE user LIKE '~%' LIMIT 5")
q("sample ipv6 (masked to first two groups, as the UI shows them)", "SELECT DISTINCT CONCAT(SUBSTRING_INDEX(user, ':', 2), ':…') FROM revision WHERE user LIKE '%:%' AND anonymous LIMIT 5")
q("anonymous but not IP-shaped", """SELECT user, COUNT(*) FROM revision WHERE anonymous
  AND NOT user REGEXP '^[0-9]{1,3}(\\\\.[0-9]{1,3}){3}$' AND user NOT LIKE '%:%' GROUP BY user ORDER BY 2 DESC LIMIT 10""")
