# States & trust

## State matrix (P2.11)

Every page has to handle every row. The fallback column is what the user sees, and it is never a stack trace.

| Situation | Landing | Investigation | Incident | Article |
|---|---|---|---|---|
| **Loading** | n/a | stage tracker + 3 skeleton cards | skeleton header + diff block | skeleton chart (grey bars) |
| **Partial** | n/a | cards usable while the run continues | n/a | n/a |
| **Empty** | out-of-scope `EmptyState` with nearest topics | “No disputed insertions found” + what was checked | n/a (404 → NotFound with link back to topic) | “No edits in this range” |
| **Corpus down** | `StatusBanner` error, search disabled, cached topics still clickable | cached run opens; a new run shows an error panel with *Retry* | cached opens | error panel |
| **Embedder down** | search disabled with explanation; chips for cached topics work | same | n/a | n/a |
| **LLM down / rate-limited** | n/a | `StatusBanner` warn: “Assessments unavailable — showing removed edits only.” Incidents capped at tier 1 | Assessment panel says “Assessment unavailable. Try again later.” | n/a |
| **Slow (> 20 s per stage)** | n/a | the stage shows “Still working — large article (3,104 revisions)”; *Cancel* always works | n/a | n/a |
| **Circuit reconnect** | styled reconnect dialog: “Reconnecting to the investigation…” | re-attach from URL + SQLite cache, no restart | from URL | from URL |
| **Run failed** | n/a | error panel per failed stage, partial results kept, *Retry stage* | n/a | n/a |

Technical detail (exception type, stage, correlation id) goes **behind a `<details>`** in the error panel, never in the headline.

**Blazor specifics.** Start the run in `OnAfterRenderAsync(firstRender)` or from an explicit event, never in `OnInitialized`, because prerender would start two runs. Batch pipeline events to about 10 re-renders a second with `InvokeAsync(StateHasChanged)`. Cancel and unsubscribe in `Dispose`, and a new search cancels the previous run.

## Trust patterns (P2.10)

| Pattern | Where | Component |
|---|---|---|
| **Why flagged.** The tier explains itself in one sentence (“Disputed and removed — because: …”), followed by each signal as a plain-language, expandable line that leads to its evidence | every incident card (as chips) and the incident page (full panel) | `WhyFlagged`, `SignalChip` |
| **Automated-assessment label** wherever model output appears, with the quoted evidence beside it and the same span highlighted in the diff | incident page | `AssessmentNote` |
| **Machine-translation tag** on every translated passage, with the original one click away | claim, diff, edit summaries, article titles | `MtLabel` |
| **Evidence links** to the exact revision on fi.wikipedia.org | event rows, card footer, quoted spans | `RevisionLink` (inside `EditSequence`) |
| **One vocabulary**, from one `TierBadge`, with no ad-hoc tier strings anywhere | everywhere | `TierBadge` |
| **What we didn’t flag**: the collapsed list of considered-but-dismissed reverts | overview | `UnflaggedList` |
| **“This looks wrong”**: stores a note (P4.4) | incident actions | n/a |

### Signal phrasebook (plain language for chips and the Why panel)

| Signal id (pipeline) | Chip | Why-panel sentence |
|---|---|---|
| `reinserted_after_revert` | Re-inserted after revert ×3 | The same text was put back 3 times after editors removed it. |
| `citation_removed` | Removed a citation | The edit deleted a source (`<ref>` to Tanner 1950) that supported the previous wording. |
| `removal_reason_accuracy` | Removed as unsourced | Editors gave “lähteetön” (*unsourced*) as the reason for removal. |
| `new_accounts` | New accounts | 2 of 3 inserting accounts were created within 9 days of their first insertion. |
| `cross_article` | In 2 articles | The same claim was inserted into *Talvisota* and *Mainilan laukaukset*. |
| `number_changed` | Changed a number | A figure changed from 26,662 to 126,000 without a new source. |
| `anonymous` | Anonymous edits | Inserted from IP addresses without an account. |

## Copy rules for review rounds (P2.12)

The copy reviewer checks for:

- **Overclaiming:** any verb of intent (“tried to deceive”, “pushed propaganda”).
- **Jargon:** “revert”, “sha1”, “tier 2” and “LLM” do not appear in UI copy. Write “removed”, “level”, “automated assessment” instead.
- **Unexplained Finnish:** every Finnish string needs an EN neighbour or an MT toggle.
- **Missing UTC** on timestamps.

## Attribution footer (every page)

> Wikipedia text © its contributors, CC BY-SA 4.0, quoted from fi.wikipedia.org revisions linked on each passage. Data: Wikimedia dumps, FineWiki, MariaDB Foundation datasets; embeddings: bge-m3. Translations and assessments are automated and can be wrong. [How this works](/methodology)
