# AssessmentNote
Model output, clearly labelled as an automated assessment: category, reasoning, the quoted evidence (also highlighted in the diff), confidence and an uncertainty note.

- Sits on `surface-sunken` so it never looks like the app's own statement.
- Confidence shows as a 3-step meter plus a word (low / medium / high), mapped from 0–1 by fixed cut points (<0.4, <0.75).
- `Assessment == null` → “Assessment unavailable. The language model could not be reached. The removal history above still stands.” plus *Retry*.
