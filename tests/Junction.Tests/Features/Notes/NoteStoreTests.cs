using Junction.Tests.Infrastructure;
using Junction.Web.Features.Notes;

namespace Junction.Tests.Features.Notes;

public sealed class NoteStoreTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly NoteStore _store;

    public NoteStoreTests() => _store = new NoteStore(_db.Factory, TimeProvider.System);

    [Fact]
    public async Task Add_trims_and_persists_note()
    {
        var note = await _store.AddAsync("  hello  ");

        Assert.Equal("hello", note.Text);
        var all = await _store.AllAsync();
        Assert.Equal(note, Assert.Single(all));
    }

    [Fact]
    public async Task All_returns_newest_first()
    {
        await _store.AddAsync("old");
        await Task.Delay(5);
        await _store.AddAsync("new");

        var all = await _store.AllAsync();

        Assert.Equal(["new", "old"], all.Select(n => n.Text));
    }

    [Fact]
    public async Task Add_rejects_blank_text() =>
        await Assert.ThrowsAsync<ArgumentException>(() => _store.AddAsync("   "));

    [Fact]
    public async Task Remove_deletes_and_raises_changed()
    {
        var note = await _store.AddAsync("x");
        var raised = false;
        _store.Changed += () => raised = true;

        Assert.True(await _store.RemoveAsync(note.Id));
        Assert.True(raised);
        Assert.Empty(await _store.AllAsync());
    }

    public void Dispose() => _db.Dispose();
}
