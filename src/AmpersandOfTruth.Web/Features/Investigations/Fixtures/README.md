# Sample incident fixtures (`Features/Investigations/Fixtures`)

Hand-written sample topics for P2 (`Investigations:Source = Fixtures`, issue #98 §1).
In fixture mode `HybridFixtureRunner` runs stages 1–2 (`ITopicSearch`, `ICandidateSource`) against the real corpus and replays
these sample incidents for stages 3–4; `IncidentMapper` derives diff passages, article titles, and masked editor labels from
the corpus (falling back to the fields in the JSON when the corpus tunnel is down).

## File location and embedding

Place one JSON file per topic at `src/AmpersandOfTruth.Web/Features/Investigations/Fixtures/<slug>.json`.
`AmpersandOfTruth.Web.csproj` embeds every `Fixtures/*.json` resource automatically, and `SampleIncidents` loads them on startup.

## File format

```json
{
  "slug": "winter-war",
  "title": "Winter War",
  "aliases": ["talvisota", "talvisota 1939"],
  "incidents": [
    {
      "id": "1256-1999",
      "page_id": 1256,
      "other_page_ids": [],
      "inserting_rev_ids": [1999],
      "removing_rev_ids": [2000],
      "tier": "contested",
      "score": 24.5,
      "claim_en": "Finland attacked the Soviet Union first, at Mainila.",
      "claim_fi": "Suomi hyökkäsi ensin.",
      "articles": [
        { "page_id": 1256, "title_fi": "Talvisota", "title_en": "Winter War" }
      ],
      "first_inserted_at": "2024-01-02T21:19:00Z",
      "last_removed_at": "2024-01-02T21:20:00Z",
      "total_exposure_seconds": 60,
      "edits": [
        {
          "rev_id": 1999,
          "parent_rev_id": 1998,
          "page_id": 1256,
          "user_display": "192.0.x.x (anonymous)",
          "is_anonymous": true,
          "timestamp": "2024-01-02T21:19:00Z",
          "role": "inserted",
          "summary": "korjaus"
        },
        {
          "rev_id": 2000,
          "parent_rev_id": 1999,
          "page_id": 1256,
          "user_display": "Rollbacker",
          "is_anonymous": false,
          "timestamp": "2024-01-02T21:20:00Z",
          "role": "removed",
          "summary": "Hylättiin muutokset"
        }
      ],
      "signals": [
        { "kind": "removal_reason", "label": "removed as inaccurate", "evidence": null },
        { "kind": "claim_content", "label": "automated assessment: a factual or narrative claim", "evidence": "kun Suomi hyökkäsi ensin" }
      ],
      "assessment": {
        "category": "false_factual_claim",
        "confidence": 4,
        "what_changed_en": "Rewrote who started the war.",
        "quotes": ["kun Suomi hyökkäsi ensin"],
        "has_verified_quote": true,
        "uncertainty_note": null,
        "historical_note_en": null
      }
    }
  ]
}
```

### Rules

- **Top-level fields:** `slug` (non-empty string), `title` (non-empty string), `aliases` (optional array of strings), `incidents` (array of incident objects).
- **Topic matching:** `SampleIncidents.Match(topicOrSlug)` compares `TopicSlug.From` of the query against `slug`, `title`, and every entry in `aliases`.
- **Incident schema:** Each element of `incidents` must validate against `research/eval/schemas/incident.schema.json` and deserialise into `IncidentRecord` via `IncidentJson.Options` (snake_case properties and enum values, UTC ISO-8601 timestamps ending in `Z`).
  - Required incident fields: `page_id` (≥ 1), `inserting_rev_ids` (non-empty, unique integers ≥ 1), `removing_rev_ids` (non-empty integers ≥ 1), `tier` (`"reverted" | "contested" | "campaign"`), `score` (number).
  - Never include raw IP addresses in `edits[].user_display` or `edits[].summary` — use `EditorDisplay` masked text (`85.76.x.x (anonymous)`).
- **Validation:** `scripts/verify.sh` runs `SampleIncidentsTests`, which checks every embedded `Fixtures/*.json` file against `incident.schema.json`, unique `inserting_rev_ids`, and valid `tier` values.
