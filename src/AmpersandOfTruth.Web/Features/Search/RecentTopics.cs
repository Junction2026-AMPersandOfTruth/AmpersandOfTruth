using System.Text.Json;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Microsoft.JSInterop;

namespace AmpersandOfTruth.Web.Features.Search;

public sealed record RecentTopic(string Label, string Slug);

/// <summary>
/// Per-browser recent investigations stored in <c>localStorage["aot.recent"]</c> (max 6, most recent first,
/// deduped by slug). Prerendering and JS interop failures degrade silently to an empty list.
/// </summary>
public sealed class RecentTopics(IJSRuntime js)
{
    public const string StorageKey = "aot.recent";
    public const int MaxItems = 6;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly List<RecentTopic> _items = [];
    private bool _loaded;

    public IReadOnlyList<RecentTopic> Items => _items;

    public event Action? Changed;

    public async Task LoadAsync()
    {
        var previousCount = _items.Count;
        var parsed = await ReadFromStorageAsync();
        _items.Clear();
        _items.AddRange(parsed);
        _loaded = true;

        if (_items.Count > 0 || previousCount > 0)
        {
            Changed?.Invoke();
        }
    }

    public async Task AddAsync(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;

        var label = topic.Trim();
        var slug = TopicSlug.From(label);
        if (string.IsNullOrEmpty(slug)) return;

        if (!_loaded)
        {
            var existing = await ReadFromStorageAsync();
            _items.Clear();
            _items.AddRange(existing);
            _loaded = true;
        }

        _items.RemoveAll(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, new RecentTopic(label, slug));
        if (_items.Count > MaxItems)
        {
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        }

        try
        {
            var json = JsonSerializer.Serialize(_items, JsonOptions);
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
        catch
        {
            // Prerender, disconnected circuit, or blocked storage.
        }

        Changed?.Invoke();
    }

    private async Task<List<RecentTopic>> ReadFromStorageAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrWhiteSpace(raw)) return [];

            var entries = JsonSerializer.Deserialize<List<RecentTopic>>(raw, JsonOptions);
            if (entries is null || entries.Count == 0) return [];

            var result = new List<RecentTopic>(Math.Min(entries.Count, MaxItems));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Label) || string.IsNullOrWhiteSpace(entry.Slug))
                {
                    continue;
                }

                var normalizedSlug = TopicSlug.From(entry.Slug);
                if (string.IsNullOrEmpty(normalizedSlug) || !seen.Add(normalizedSlug))
                {
                    continue;
                }

                result.Add(new RecentTopic(entry.Label.Trim(), normalizedSlug));
                if (result.Count == MaxItems)
                {
                    break;
                }
            }

            return result;
        }
        catch
        {
            return [];
        }
    }
}
