# P2.12 Review Walk Report

- **UTC timestamp:** 2026-11-14T20:47:35.487Z
- **Mode:** standard
- **Target URL:** `https://asot.tech`
- **Browser:** `/usr/bin/google-chrome` (Chromium 157.0.8103.78)
- **Summary:** 18/20 passed · 2 skipped · 0 failed · 0/20 steps with banners · 2 console error(s) · 1 failed request(s) · 0 horizontal scroll flag(s)

## Step × Viewport Matrix

| Step | Viewport | Status | Banners | Screenshot | Timing (s) | Console / Failed Reqs | Overflow |
|---|---|---|---|---|---:|---|---|
| 01 Landing `/` | desktop (1280×720) | ok | — | [01-landing-desktop.png](screens/01-landing-desktop.png) | 1.69 | 0 | ok |
| 02 Click first `topic-chip` | desktop (1280×720) | ok | — | [02-topic-chip-desktop.png](screens/02-topic-chip-desktop.png) | 0.12 | 0 | ok |
| 03 Wait for `summary-header` + `incident-card` or empty state | desktop (1280×720) | ok | — | [progress](screens/03-progress-desktop.png), [03-overview-desktop.png](screens/03-overview-desktop.png) | 2.77 | 0 | ok |
| 04 Click first `incident-card` | desktop (1280×720) | ok | — | [04-incident-desktop.png](screens/04-incident-desktop.png) | 0.32 | 0 | ok |
| 05 Toggle `diff-view-toggle` | desktop (1280×720) | ok | — | [05-diff-toggle-desktop.png](screens/05-diff-toggle-desktop.png) | 0.16 | 0 | ok |
| 06 Follow first article link to `/articles/...` | desktop (1280×720) | ok | — | [06-article-desktop.png](screens/06-article-desktop.png) | 0.29 | 0 | ok |
| 07 Browser back | desktop (1280×720) | ok | — | [07-back-desktop.png](screens/07-back-desktop.png) | 0.18 | 0 | ok |
| 08 Landing, type "Mannerheim" in `topic-input`, Enter | desktop (1280×720) | ok | — | [08-mannerheim-desktop.png](screens/08-mannerheim-desktop.png) | 1.99 | 0 | ok |
| 09 Landing, type "climate change", Enter (out-of-scope or results) | desktop (1280×720) | ok | — | [09-climate-change-desktop.png](screens/09-climate-change-desktop.png) | 1.44 | 0 | ok |
| 10 `/methodology` | desktop (1280×720) | ok | — | [10-methodology-desktop.png](screens/10-methodology-desktop.png) | 1.22 | 0 | ok |
| 01 Landing `/` | phone (390×844 (@2x touch)) | ok | — | [01-landing-phone.png](screens/01-landing-phone.png) | 1.36 | 0 | ok |
| 02 Click first `topic-chip` | phone (390×844 (@2x touch)) | ok | — | [02-topic-chip-phone.png](screens/02-topic-chip-phone.png) | 0.15 | 0 | ok |
| 03 Wait for `summary-header` + `incident-card` or empty state | phone (390×844 (@2x touch)) | ok | — | [progress](screens/03-progress-phone.png), [03-overview-phone.png](screens/03-overview-phone.png) | 3.47 | 0 | ok |
| 04 Click first `incident-card` | phone (390×844 (@2x touch)) | ok | — | [04-incident-phone.png](screens/04-incident-phone.png) | 0.44 | 0 | ok |
| 05 Toggle `diff-view-toggle` | phone (390×844 (@2x touch)) | skipped: missing or hidden [data-testid="diff-view-toggle"] | — | [05-diff-toggle-phone.png](screens/05-diff-toggle-phone.png) | 2.71 | 0 | ok |
| 06 Follow first article link to `/articles/...` | phone (390×844 (@2x touch)) | ok | — | [06-article-phone.png](screens/06-article-phone.png) | 0.21 | 0 | ok |
| 07 Browser back | phone (390×844 (@2x touch)) | ok | — | [07-back-phone.png](screens/07-back-phone.png) | 0.27 | 0 | ok |
| 08 Landing, type "Mannerheim" in `topic-input`, Enter | phone (390×844 (@2x touch)) | skipped: locator.fill: Timeout 30000ms exceeded. | — | [08-mannerheim-phone.png](screens/08-mannerheim-phone.png) | 30.76 | 2 console, 1 req | ok |
| 09 Landing, type "climate change", Enter (out-of-scope or results) | phone (390×844 (@2x touch)) | ok | — | [09-climate-change-phone.png](screens/09-climate-change-phone.png) | 1.49 | 0 | ok |
| 10 `/methodology` | phone (390×844 (@2x touch)) | ok | — | [10-methodology-phone.png](screens/10-methodology-phone.png) | 1.14 | 0 | ok |

## Console Errors & Failed Requests

### Step 08 (mannerheim) — phone
- **console:** `Failed to load resource: the server responded with a status of 502 ()`
- **console:** `pageerror: Unexpected token 'B', "Bad Gateway" is not valid JSON`
- **request:** `HTTP 502 GET https://asot.tech/_blazor/initializers`

