using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.JSInterop;

namespace AmpersandOfTruth.Web.Features.Library;

/// <summary>
/// Scoped per-browser capability key persisted in <c>localStorage["aot-owner"]</c>.
/// Must only be accessed after the first interactive render (never during static prerendering).
/// </summary>
public sealed class OwnerKey(IJSRuntime js)
{
    public const string StorageKey = "aot-owner";
    private const int MaxLength = 64;

    private string? _cached;

    public string? Peek => _cached;

    /// <summary>Seeds an explicit owner key in-memory (for deterministic component tests).</summary>
    public void Seed(string ownerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        var trimmed = ownerKey.Trim();
        _cached = trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength];
    }

    public async ValueTask<string> GetOrCreateAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_cached))
        {
            return _cached;
        }

        try
        {
            var stored = await js.InvokeAsync<string?>("localStorage.getItem", ct, StorageKey);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                var trimmed = stored.Trim();
                if (trimmed.Length <= MaxLength)
                {
                    _cached = trimmed;
                    return _cached;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Disconnected circuit or blocked storage → generate an ephemeral key for this circuit.
        }

        var generated = GenerateKey();
        _cached = generated;

        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", ct, StorageKey, generated);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Blocked storage → keep in-memory key for this circuit.
        }

        return _cached;
    }

    public static string GenerateKey()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url.EncodeToString(bytes);
    }
}
