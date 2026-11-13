using Junction.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Junction.Web.Features.Notes;

/// <summary>
/// Singleton: holds no DbContext, creates one per operation via the factory.
/// <see cref="Changed"/> lets every open page refresh live when any user edits.
/// </summary>
public sealed class NoteStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider clock)
{
    public event Action? Changed;

    public async Task<IReadOnlyList<Note>> AllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Set<Note>().AsNoTracking().OrderByDescending(n => n.CreatedAt).ToListAsync(ct);
    }

    public async Task<Note> AddAsync(string text, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var note = new Note(Guid.NewGuid(), text.Trim(), clock.GetUtcNow());

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Add(note);
        await db.SaveChangesAsync(ct);

        Changed?.Invoke();
        return note;
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var removed = await db.Set<Note>().Where(n => n.Id == id).ExecuteDeleteAsync(ct) > 0;

        if (removed) Changed?.Invoke();
        return removed;
    }
}
