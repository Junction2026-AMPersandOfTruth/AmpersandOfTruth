using Microsoft.JSInterop;

namespace AmpersandOfTruth.Web.Features.Translation;

/// <summary>
/// Per-circuit UI language preference (<c>"en"</c> or <c>"fi"</c>, default <c>"en"</c>), persisted in browser
/// <c>localStorage["aot.lang"]</c>. Prerendering and JS interop failures fall back silently to the current value.
/// </summary>
public sealed class LanguagePreference
{
    public const string StorageKey = "aot.lang";
    public const string English = "en";
    public const string Finnish = "fi";

    public string Lang { get; private set; } = English;

    public event Action? Changed;

    public async Task LoadAsync(IJSRuntime js)
    {
        ArgumentNullException.ThrowIfNull(js);

        try
        {
            var stored = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (stored is not null)
            {
                var normalized = Normalize(stored);
                if (!string.Equals(Lang, normalized, StringComparison.Ordinal))
                {
                    Lang = normalized;
                    Changed?.Invoke();
                }
            }
        }
        catch
        {
            // Static SSR prerender, disconnected circuit, or blocked storage → keep default.
        }
    }

    public async Task SetAsync(IJSRuntime js, string lang)
    {
        ArgumentNullException.ThrowIfNull(js);

        var normalized = Normalize(lang);
        Lang = normalized;

        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, normalized);
        }
        catch
        {
            // Prerender, disconnected circuit, or blocked storage → keep in-memory preference.
        }

        Changed?.Invoke();
    }

    private static string Normalize(string? lang) =>
        string.Equals(lang?.Trim(), Finnish, StringComparison.OrdinalIgnoreCase)
            ? Finnish
            : English;
}
