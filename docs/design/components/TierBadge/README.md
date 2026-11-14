# TierBadge
The one place a tier is named: P0.7 wording, 1–3 pips and a fill, so the tier never relies on colour alone.

- **Tiers:** `reverted` (grey, 1 pip) · `contested` (warm tint, 2) · `campaign` (plum-grey tint, 3). All are pale tints with dark text, so a badge informs without shouting. The level is read from the pips and the words. Wording is fixed: *Removed by Wikipedia editors* · *Disputed and removed* · *Pattern consistent with a coordinated attempt*.
- `ShowWhy` appends “· why this level?”, which links to the Why panel on the incident page and to `/methodology#levels` elsewhere.
- Never write tier strings anywhere else. Filters, legends and exports call `TierText.For(tier)`.
- Razor: `<TierBadge Tier="Tier.Campaign" ShowWhy />` → `data-testid="tier-badge"`.
