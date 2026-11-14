# <Task title — imperative, one line>

## Goal
<1–3 sentences: what the user can do afterwards, and why it matters for the demo.>

## Scope
Create / modify only:
- `src/AmpersandOfTruth.Web/Features/<Name>/...`
- `tests/AmpersandOfTruth.Tests/Features/<Name>/...`
- <one-line registrations: Program.cs `AddXFeature()`, NavMenu link, AppSmokeTests route — list explicitly if allowed>

Do NOT touch: <other features, shared components, etc.>

## Contracts
<Exact names the rest of the codebase / other workers depend on: routes, public types and method
signatures, entity fields + types, data-testid values. Be concrete — this is what prevents rework.>

## Implementation notes
<Pointers, not prose: which existing file to copy, lifetimes, edge cases, what to stub/fake.
New NuGet packages allowed: none | <list>.>

## Acceptance criteria
- [ ] <observable behaviour 1 — each one should map to a test>
- [ ] <observable behaviour 2>
- [ ] Tests: <which service tests / bUnit tests are expected>
- [ ] `scripts/verify.sh` green
