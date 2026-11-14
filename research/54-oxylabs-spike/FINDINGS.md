# P3.1 · Oxylabs capability note (#54) — draft for the issue

Measured 2026-11-14 with the hackathon key (`scripts/oxylabs-spike.sh`, then the app's own client and tracer).

## Verdict: **go** for P3, on the Web API, with search-engine dates treated as approximate

## What our access is

- **Product: Oxylabs Web API** (one API key, Bearer, `webapi.oxylabs.io`). Two endpoints: `/v1/search` (web search) and
  `/v1/scrape` (page fetch). No Web Scraper API username/password, so **no date-restricted search, no News vertical, no
  Bing** — those exist only in the Web Scraper API (`google_search` + `tbs=cdr:1,…`). Worth asking Oxylabs/the organisers
  for Scraper credentials if dated search matters; the app already supports both (`OxylabsOptions.EffectiveMode`).
- **Synchronous** requests. Rate headers: `search_total_dynamic_europe-…-limit: 10` and `total_default-…-limit: 50`; the
  remaining count resets between calls, so these are **rate limits per window, not a weekend quota**. 4 fetches in
  parallel never hit a 429 across ~150 live calls.
- Faulted fetches are **not charged** (Oxylabs: "you don't pay for faulted scrapes").

## Measured

| | Result |
|---|---|
| Search latency | 1.9–3.3 s, 10 results |
| Per-result date | **Yes, inside the snippet**: `outline` is a list of strings, the first starts with Google's date (`"September 16, 2026 - …"`, also `"1 week ago - …"`). 7 of 10 results dated. (Parser fixed in `05f4a9c`.) |
| Date-restricted search | Not available on the Web API (the app runs one search per query and dates pages afterwards) |
| Page fetch | 4–8 s typical, up to 17 s; returns `html` + `markdown` + `metadata` |
| Text + date extractable | Yes: markdown is clean main text; dates from JSON-LD / meta / `<time>` / URL path / Wayback. Wikipedia: JSON-LD `datePublished` |
| JS rendering / geo | `run_js` and `location` parameters exist; not needed for the pages tried |
| Failure mode | Login-walled sites (facebook.com, researchgate.net): HTTP 500 `REQUEST_FAILED_AFTER_TOO_MANY_RETRIES` after ~40 s. Not retried by us since `05f4a9c` |

## Per-incident budget

One trace = **5 searches + ~25 page fetches ≈ 30 requests, 55–70 s cold** (fetch-bound: ~5 s per page, 4 in parallel),
**instant when replayed** (every OK response is cached; the finished result is stored per incident).
Caps in `appsettings.json`: `Oxylabs:PerScopeRequestCap` 60 per incident, `DailyRequestCap` 1,000.

## End-to-end check

- Hand-written claim ("Finland fired the Mainila shots"): 22 sources; 21 argue against it (euvsdisinfo.eu, Yle, IS,
  Iltalehti, MTV, KSML…), 1 reports it with attribution; Wikipedia mirrors recognised.
- Real incident (Talvisota `ww-01`, the infobox result rewrite of 27 Aug 2010): 1 page carrying the claim, a 2009 KSML
  article "Talvisota oli torjuntavoitto" dated *before* the edit, with the search engine/metadata disagreement shown.

## Terms

Not checked in this spike — someone should read the hackathon's Oxylabs terms before the demo. We only fetch pages a
user asks about, cache them locally, never republish page text beyond a short quoted passage with a link.
