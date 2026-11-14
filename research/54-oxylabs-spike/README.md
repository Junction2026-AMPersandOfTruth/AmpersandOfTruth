# P3.1 · Oxylabs spike (#54)

What our Oxylabs access gives us, measured, before P3 is designed around it. Branch `oxylabs` only.

## Run

Put the credentials in the app's user-secrets (never in the repo or chat), then run the spike:

```bash
# Web API (one API key)
dotnet user-secrets set "Oxylabs:ApiKey" "<key>" --project src/AmpersandOfTruth.Web
# or Web Scraper API (dashboard username + password)
dotnet user-secrets set "Oxylabs:Username" "<user>" --project src/AmpersandOfTruth.Web
dotnet user-secrets set "Oxylabs:Password" "<password>" --project src/AmpersandOfTruth.Web

scripts/oxylabs-spike.sh                       # default: "Mainilan laukaukset", window 2014, fetch 3 pages
scripts/oxylabs-spike.sh --query '"Lapuan liike"' --from 1929-01-01 --to 1932-12-31
```

It writes `out/<timestamp>/report.md` plus one raw JSON per call (`out/` is git-ignored: responses contain third-party
page content). Paste `report.md` on #54 with the go/no-go.

Then the app's own client, end to end:

```bash
OXYLABS_TESTS=1 dotnet test --filter Category=Oxylabs --logger "console;verbosity=detailed"
scripts/webspread.sh claim < research/54-oxylabs-spike/example-claim.json     # + Gemini:ApiKey for the LLM stages
scripts/webspread.sh topic "Talvisota" --rank 1                               # a real incident (corpus tunnel + Gemini)
```

## What it checks (the issue's list)

| Question | Web Scraper API | Web API |
|---|---|---|
| Search works, latency | `google_search`, exact Finnish phrase | `/v1/search` |
| Date-restricted search | `context tbs=cdr:1,cd_min,cd_max` → does the result set change? | not documented (the app treats it as unsupported) |
| Per-result date field | fields of `organic[]` | fields of `results[]` |
| News vertical | `context tbm=nws` | — |
| Other engines | `bing_search` | — |
| Page fetch: text + date extractable | `universal` → HTML → JSON-LD / meta / `<time>` / URL | `/v1/scrape` `output: [markdown, html]` |

`example-claim.json` is an illustrative claim (the Mainila shots, exposure window made up), for exercising the pipeline.

## Unverified in the app's client until this has run

See the summary on `Features/WebSpread/Oxylabs/OxylabsWire.cs`. If the spike shows a different shape, fix `OxylabsWire`
and `OxylabsClientTests` together.
