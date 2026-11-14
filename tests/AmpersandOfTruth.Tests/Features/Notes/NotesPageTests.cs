using Bunit;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Notes;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.Notes;

public class NotesPageTests : BunitContext
{
    private readonly TestDb _db = new();

    public NotesPageTests()
    {
        _db.AddTo(Services);
        Services.AddSingleton(TimeProvider.System).AddNotesFeature();
    }

    [Fact]
    public void Shows_empty_state_initially()
    {
        var cut = Render<NotesPage>();

        cut.WaitForElement("[data-testid=empty-state]").MarkupMatches(
            """<div class="text-center text-muted py-5 border rounded" data-testid="empty-state">No notes yet.</div>""");
    }

    [Fact]
    public void Submitting_form_adds_note_to_list()
    {
        var cut = Render<NotesPage>();
        cut.WaitForElement("[data-testid=empty-state]");

        cut.Find("[data-testid=note-input]").Change("Buy coffee");
        cut.Find("form").Submit();

        // DB I/O is async → wait for the re-render instead of asserting immediately.
        cut.WaitForAssertion(() =>
        {
            var item = Assert.Single(cut.FindAll("[data-testid=note-list] li"));
            Assert.Contains("Buy coffee", item.TextContent);
        });
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        _db.Dispose();
    }
}
