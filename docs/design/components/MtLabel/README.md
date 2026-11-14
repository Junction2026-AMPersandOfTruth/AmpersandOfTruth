# MtLabel
Labels any machine-translated text and keeps the original one click away. Required wherever translated text appears.

- Short form: the dashed tag *Machine translation* plus “Show original”, which toggles the text in place.
- Stacked form: English first, then the original under it, prefixed with a language tag (`.aot-original` with a `lang` attribute). Cards and the incident header use this form.
- Article titles prefer the English sibling article’s real title (via `wikidata_id`). Only a machine-translated title gets the tag.
