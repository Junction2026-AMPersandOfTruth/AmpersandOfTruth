#!/usr/bin/env python3
"""P3.1 spike (#54): what does our Oxylabs access actually give us?  Stdlib only.

Runs the issue's "try by hand" list against whichever Oxylabs product the credentials belong to and writes
raw responses + a Markdown capability report:

  Web Scraper API (username + password, realtime.oxylabs.io):
    1. google_search, exact Finnish phrase, parsed, no date range
    2. the same with a Google custom date range (context tbs=cdr:1,...)  -> does the result set change? dates?
    3. the same on the News vertical (context tbm=nws)
    4. bing_search, same phrase                                      -> engine coverage
    5. universal fetch of the top 3 result pages                      -> main text + publication date extractable?
  Web API (API key, webapi.oxylabs.io):
    1. /v1/search, same phrase   2. /v1/scrape of the top 3 results (markdown + html)

Credentials (never printed or written): env OXYLABS_USERNAME + OXYLABS_PASSWORD and/or OXYLABS_API_KEY, else the
app's user-secrets (Oxylabs:Username / Oxylabs:Password / Oxylabs:ApiKey) via `dotnet user-secrets list`.

Usage:  scripts/oxylabs-spike.sh [--query '"Mainilan laukaukset"'] [--from 2014-01-01 --to 2014-12-31] [--fetch 3]
Output: research/54-oxylabs-spike/out/<UTC timestamp>/  (git-ignored) — report.md + one JSON file per call.
"""
import argparse
import base64
import datetime as dt
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WEB = ROOT / "src" / "AmpersandOfTruth.Web"
SCRAPER_URL = "https://realtime.oxylabs.io/v1/queries"
WEBAPI_URL = "https://webapi.oxylabs.io/v1/"


def secrets():
    creds = {
        "username": os.environ.get("OXYLABS_USERNAME"),
        "password": os.environ.get("OXYLABS_PASSWORD"),
        "api_key": os.environ.get("OXYLABS_API_KEY"),
    }
    if any(creds.values()):
        return creds
    try:
        out = subprocess.run(["dotnet", "user-secrets", "list", "--project", str(WEB)],
                             capture_output=True, text=True, timeout=60).stdout
    except (OSError, subprocess.TimeoutExpired):
        out = ""
    for line in out.splitlines():
        key, _, value = line.partition(" = ")
        if key.strip() == "Oxylabs:Username": creds["username"] = value.strip()
        if key.strip() == "Oxylabs:Password": creds["password"] = value.strip()
        if key.strip() == "Oxylabs:ApiKey": creds["api_key"] = value.strip()
    return creds


def post(url, body, auth_header, timeout=180):
    data = json.dumps(body).encode()
    req = urllib.request.Request(url, data=data, method="POST",
                                 headers={"Content-Type": "application/json", "Authorization": auth_header})
    t0 = time.monotonic()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            status, raw, headers = r.status, r.read().decode("utf-8", "replace"), dict(r.headers)
    except urllib.error.HTTPError as e:
        status, raw, headers = e.code, e.read().decode("utf-8", "replace"), dict(e.headers or {})
    except (urllib.error.URLError, TimeoutError) as e:
        return {"status": None, "error": str(e), "seconds": round(time.monotonic() - t0, 2)}
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError:
        payload = raw[:2000]
    return {"status": status, "seconds": round(time.monotonic() - t0, 2), "job_id": headers.get("x-oxylabs-job-id"),
            "rate_headers": {k: v for k, v in headers.items() if "limit" in k.lower() or "remaining" in k.lower()},
            "body": payload}


def organic(resp):
    """Organic results from a parsed google/bing response, wherever they sit."""
    try:
        content = resp["body"]["results"][0]["content"]
    except (KeyError, IndexError, TypeError):
        return []
    if isinstance(content, dict):
        res = content.get("results", content)
        if isinstance(res, dict):
            return res.get("organic") or []
    return []


DATE_PATTERNS = [
    ("json-ld datePublished", r'"datePublished"\s*:\s*"([^"]+)"'),
    ("meta article:published_time", r'article:published_time"[^>]*content="([^"]+)"'),
    ("meta (reversed) published_time", r'content="([^"]+)"[^>]*article:published_time'),
    ("<time datetime>", r'<time[^>]*datetime="([^"]+)"'),
    ("url path", None),
]


def dates_in(url, html):
    found = []
    for label, pattern in DATE_PATTERNS:
        if pattern is None:
            m = re.search(r"/((?:19|20)\d\d)[/-](\d{1,2})(?:[/-](\d{1,2}))?(?:[/-]|$)", url)
            if m: found.append((label, "-".join(g for g in m.groups() if g)))
        elif html and (m := re.search(pattern, html, re.I)):
            found.append((label, m.group(1)))
    return found


def text_of(html):
    html = re.sub(r"(?is)<(head|script|style|nav|header|footer|aside|form)\b.*?</\1\s*>", " ", html or "")
    return re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", html)).strip()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--query", default='"Mainilan laukaukset"')
    ap.add_argument("--from", dest="date_from", default="2014-01-01")
    ap.add_argument("--to", dest="date_to", default="2014-12-31")
    ap.add_argument("--fetch", type=int, default=3)
    args = ap.parse_args()

    creds = secrets()
    if not any(creds.values()):
        print("No Oxylabs credentials: set env OXYLABS_USERNAME + OXYLABS_PASSWORD or OXYLABS_API_KEY, or user-secrets "
              "Oxylabs:Username/Password or Oxylabs:ApiKey (scripts/oxylabs-spike.sh --help).", file=sys.stderr)
        return 2
    out = ROOT / "research" / "54-oxylabs-spike" / "out" / dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    out.mkdir(parents=True, exist_ok=True)
    report = [f"# Oxylabs spike — {dt.datetime.now(dt.timezone.utc):%Y-%m-%d %H:%M} UTC", "",
              f"Query: `{args.query}` · date window {args.date_from} … {args.date_to}", ""]
    rows = []

    def call(name, url, body, auth):
        print(f"→ {name} …", file=sys.stderr, flush=True)
        r = post(url, body, auth)
        (out / f"{name}.json").write_text(json.dumps({"request": body, "response": r}, ensure_ascii=False, indent=1))
        err = r.get("error") or (None if r["status"] == 200 else str(r.get("body"))[:200])
        rows.append((name, r["status"], r["seconds"], err))
        print(f"  {r['status']} in {r['seconds']} s{' — ' + err if err else ''}", file=sys.stderr)
        return r

    tried = False
    if creds["username"] and creds["password"]:
        tried = True
        auth = "Basic " + base64.b64encode(f"{creds['username']}:{creds['password']}".encode()).decode()
        d0, d1 = (dt.date.fromisoformat(x) for x in (args.date_from, args.date_to))
        tbs = f"cdr:1,cd_min:{d0.month}/{d0.day}/{d0.year},cd_max:{d1.month}/{d1.day}/{d1.year}"
        base = {"source": "google_search", "query": args.query, "parse": True, "domain": "fi", "geo_location": "Finland",
                "context": [{"key": "results_language", "value": "fi"}]}
        plain = call("scraper-google", SCRAPER_URL, base, auth)
        windowed = call("scraper-google-daterange", SCRAPER_URL, {**base, "context": base["context"] + [{"key": "tbs", "value": tbs}]}, auth)
        news = call("scraper-google-news", SCRAPER_URL, {**base, "context": base["context"] + [{"key": "tbm", "value": "nws"}]}, auth)
        bing = call("scraper-bing", SCRAPER_URL, {"source": "bing_search", "query": args.query, "parse": True}, auth)

        def summary(label, r):
            res = organic(r)
            keys = sorted({k for item in res for k in item})
            dated = [item for item in res if any(k in item for k in ("date", "published", "published_at"))]
            report.append(f"- **{label}**: {len(res)} organic results; fields {keys}; with a date field: {len(dated)}")
            return res

        report += ["## Web Scraper API", ""]
        p = summary("google, no window", plain)
        w = summary(f"google, tbs {tbs}", windowed)
        summary("google news (tbm=nws)", news)
        summary("bing", bing)
        overlap = len({x.get("url") for x in p} & {x.get("url") for x in w})
        report.append(f"- date range changed the results: {'yes' if p and w and overlap < len(p) else 'no / inconclusive'} "
                      f"(overlap {overlap} of {len(p)})")
        report.append("")

        report += ["### Page fetches (universal)", ""]
        for i, item in enumerate((p or w)[: args.fetch]):
            url = item.get("url")
            r = call(f"scraper-fetch-{i + 1}", SCRAPER_URL, {"source": "universal", "url": url, "geo_location": "Finland"}, auth)
            try:
                html = r["body"]["results"][0]["content"]
            except (KeyError, IndexError, TypeError):
                html = ""
            report.append(f"- {url}: HTTP {r['status']}, {r['seconds']} s, text {len(text_of(html))} chars, "
                          f"dates {dates_in(url, html) or 'none found'}")
        report.append("")

    if creds["api_key"]:
        tried = True
        auth = f"Bearer {creds['api_key']}"
        report += ["## Web API", ""]
        s = call("webapi-search", WEBAPI_URL + "search", {"query": args.query, "max_results": 10}, auth)
        results = s.get("body", {}).get("results", []) if isinstance(s.get("body"), dict) else []
        keys = sorted({k for item in results for k in item})
        report.append(f"- /v1/search: {len(results)} results; fields {keys}")
        for i, item in enumerate(results[: args.fetch]):
            url = item.get("url")
            r = call(f"webapi-scrape-{i + 1}", WEBAPI_URL + "scrape", {"url": url, "output": ["markdown", "html"]}, auth)
            first = (r.get("body") or {}).get("results", [{}])[0] if isinstance(r.get("body"), dict) else {}
            html = first.get("html") or first.get("content") or ""
            report.append(f"- {url}: HTTP {r['status']}, {r['seconds']} s, keys {sorted(first)}, markdown "
                          f"{len(first.get('markdown') or '')} chars, dates {dates_in(url, html) or 'none found'}")
        report.append("")

    if not tried:
        print("No Oxylabs credentials: set env OXYLABS_USERNAME + OXYLABS_PASSWORD or OXYLABS_API_KEY, or user-secrets "
              "Oxylabs:Username/Password or Oxylabs:ApiKey (scripts/oxylabs-spike.sh --help).", file=sys.stderr)
        return 2

    report += ["## Calls", "", "| call | HTTP | seconds | error |", "|---|---|---|---|"]
    report += [f"| {n} | {s} | {t} | {(e or '').replace('|', '/')} |" for n, s, t, e in rows]
    report += ["", "## To decide (fill in on #54)", "",
               "- Go / no-go for P3, and which product we build on.",
               "- Per-incident request budget (searches + fetches), from the timings above.",
               "- Terms of use: anything restricting the sites or our use?"]
    (out / "report.md").write_text("\n".join(report) + "\n")
    print("\n".join(report))
    print(f"\nraw responses + report: {out}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
