using AmpersandOfTruth.Web.Components;
using AmpersandOfTruth.Web.Data;
using AmpersandOfTruth.Web.Features.Accounts;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Translation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();
builder.Services.AddAppData();

// ── Features ── one line per feature; keep registrations inside each feature folder.
builder.Services.AddCorpusFeature();
builder.Services.AddEmbeddingFeature();
builder.Services.AddAnalysisFeature();
builder.Services.AddInvestigationsFeature();
builder.Services.AddTranslationFeature();
builder.Services.AddArticlesFeature();
builder.Services.AddAccountsFeature();

var app = builder.Build();

await app.MigrateDatabaseAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

// Exposes Program to WebApplicationFactory in tests.
public partial class Program;
