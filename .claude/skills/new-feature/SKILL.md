---
name: new-feature
description: Scaffold a new vertical feature slice (page + service + DI + tests + nav + smoke route) following this repo's conventions. Use when the user asks to add a new page, screen, or feature area.
---

# New feature slice

Argument: feature name in PascalCase (e.g. `Leaderboard`) plus a short description of what it does. Ask only if the name is missing.

Use `src/AmpersandOfTruth.Web/Features/Notes/` and `tests/AmpersandOfTruth.Tests/Features/Notes/` as the template. Read them first.

## Steps

1. Create `src/AmpersandOfTruth.Web/Features/<Name>/`:
   - `<Name>Page.razor` with `@page "/<kebab-name>"`, `<PageTitle>`, an `<h1>`, and `data-testid` on interactive elements.
   - Model records + a service/store class with the actual logic (keep logic out of `.razor`).
   - If it persists data: `<Entity>Configuration.cs` (`IEntityTypeConfiguration<T>`), service injects `IDbContextFactory<AppDbContext>`.
     Do NOT edit `AppDbContext`. Then `dotnet ef migrations add Add<Name> --project src/AmpersandOfTruth.Web --output-dir Data/Migrations`.
   - `<Name>Feature.cs` with `public static IServiceCollection Add<Name>Feature(this IServiceCollection services)`.
     Choose lifetime deliberately: Singleton = shared across all users (thread-safe!), Scoped = per browser tab.
2. Register: add `builder.Services.Add<Name>Feature();` under the `── Features ──` comment in `Program.cs`.
3. Nav: add a `nav-item` block in `Components/Layout/NavMenu.razor` above the `Add new feature links` marker.
4. Tests in `tests/AmpersandOfTruth.Tests/Features/<Name>/`:
   - `<Service>Tests.cs` — xUnit facts for the service logic (use `TestDb` if it touches the DB).
   - `<Name>PageTests.cs` — bUnit (`: BunitContext`, `Render<T>()`) covering the main interaction.
   - Add the route to `[InlineData]` in `tests/AmpersandOfTruth.Tests/Smoke/AppSmokeTests.cs`.
5. Run `scripts/verify.sh`, then `scripts/smoke.sh / /<kebab-name>`. Fix until green.
6. Report: files created, route, and anything stubbed/faked that still needs real implementation.

Don't touch other feature folders.
