using System.Globalization;
using System.Text.Json;

namespace Junction.Web.Features.Analysis.Batch;

public sealed record BatchItem(string Id, IReadOnlyDictionary<string, string> Variables);

public sealed record BatchOptions(string PromptId, IReadOnlyList<string?> Models)
{
    public string? Scope { get; init; }
    public int Concurrency { get; init; } = 4;
    public CachePolicy Cache { get; init; } = CachePolicy.ReadWrite;
}

public sealed record BatchResultLine(
    string Id,
    string Prompt,
    string PromptVersion,
    string Model,
    string Outcome,
    JsonElement? Value,
    string? Error,
    bool FromCache,
    int Attempts,
    long LatencyMs,
    int InputTokens,
    int OutputTokens,
    int ThoughtTokens,
    int CachedTokens,
    double CostUsd,
    string? RawText);

public sealed record ModelBatchStats(
    string Model,
    int Items,
    int Ok,
    int Invalid,
    int Blocked,
    int Truncated,
    int Failed,
    int BudgetExceeded,
    int CacheHits,
    double AvgLatencyMs,
    double P95LatencyMs,
    long InputTokens,
    long OutputTokens,
    long ThoughtTokens,
    double CostUsd);

public sealed class AnalysisBatchRunner(IAnalysisModel model, ILogger<AnalysisBatchRunner> log)
{
    public static IReadOnlyList<BatchItem> ParseJsonl(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var items = new List<BatchItem>();
        var lineNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                throw new FormatException($"Invalid JSON on line {lineNumber}: {ex.Message}", ex);
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException($"Line {lineNumber}: expected a JSON object, got {root.ValueKind}.");
                }

                var id = ExtractId(root, "_id")
                    ?? ExtractId(root, "id")
                    ?? lineNumber.ToString(CultureInfo.InvariantCulture);

                var variables = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var prop in root.EnumerateObject())
                {
                    variables[prop.Name] = ConvertPropertyValue(prop.Value);
                }

                items.Add(new BatchItem(id, variables));
            }
        }

        return items;
    }

    public static ISet<(string Id, string Model)> ReadCompleted(TextReader existingOutput)
    {
        ArgumentNullException.ThrowIfNull(existingOutput);

        var completed = new HashSet<(string Id, string Model)>();
        string? line;
        while ((line = existingOutput.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!root.TryGetProperty("outcome", out var outcomeProp) || outcomeProp.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var outcome = outcomeProp.GetString();
            if (outcome is not ("ok" or "invalid" or "blocked" or "truncated"))
            {
                continue;
            }

            if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                && root.TryGetProperty("model", out var modelProp) && modelProp.ValueKind == JsonValueKind.String
                && idProp.GetString() is { } id
                && modelProp.GetString() is { } modelName)
            {
                completed.Add((id, modelName));
            }
        }

        return completed;
    }

    /// <summary>
    /// Runs every item against every model in <paramref name="options"/> with bounded concurrency.
    /// Pairs present in <paramref name="skip"/> are skipped, matching on <c>(item.Id, model ?? "default")</c>;
    /// when resuming from an output file that records resolved model IDs, pass explicit model IDs in <see cref="BatchOptions.Models"/>.
    /// </summary>
    public async Task<IReadOnlyList<ModelBatchStats>> RunAsync(
        IReadOnlyList<BatchItem> items,
        BatchOptions options,
        Func<BatchResultLine, Task> onResult,
        ISet<(string Id, string Model)>? skip = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onResult);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Concurrency, 1);

        if (options.Models.Count == 0)
        {
            return [];
        }

        var accumulators = new ModelStatsAccumulator[options.Models.Count];
        for (var m = 0; m < options.Models.Count; m++)
        {
            accumulators[m] = new ModelStatsAccumulator(options.Models[m]);
        }

        var work = new List<(BatchItem Item, int ModelIndex, string? RequestedModel)>(items.Count * options.Models.Count);
        foreach (var item in items)
        {
            for (var m = 0; m < options.Models.Count; m++)
            {
                var requestedModel = options.Models[m];
                var skipKey = (item.Id, requestedModel ?? "default");
                if (skip is not null && skip.Contains(skipKey))
                {
                    continue;
                }

                work.Add((item, m, requestedModel));
            }
        }

        var total = work.Count;
        var done = 0;
        var okCount = 0;
        using var resultGate = new SemaphoreSlim(1, 1);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Concurrency,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(work, parallelOptions, async (entry, loopCt) =>
        {
            var request = new AnalysisRequest(options.PromptId, entry.Item.Variables)
            {
                Model = entry.RequestedModel,
                Scope = options.Scope,
                Cache = options.Cache,
            };

            var result = await model.RunAsync<JsonElement>(request, loopCt);
            var line = ToResultLine(entry.Item.Id, result);

            await resultGate.WaitAsync(loopCt);
            try
            {
                await onResult(line);

                accumulators[entry.ModelIndex].Record(result, line.LatencyMs);
                done++;
                if (result.IsOk)
                {
                    okCount++;
                }

                if ((done % 10) == 0)
                {
                    log.LogInformation(
                        "Batch {PromptId} progress: {Done}/{Total} ({Ok} ok)",
                        options.PromptId,
                        done,
                        total,
                        okCount);
                }
            }
            finally
            {
                resultGate.Release();
            }
        });

        var stats = new ModelBatchStats[accumulators.Length];
        for (var i = 0; i < accumulators.Length; i++)
        {
            stats[i] = accumulators[i].ToStats();
        }

        return stats;
    }

    internal static BatchResultLine ToResultLine(string id, AnalysisResult<JsonElement> result)
    {
        var info = result.Info;
        var usage = info.Usage;
        return new BatchResultLine(
            Id: id,
            Prompt: info.PromptId,
            PromptVersion: info.PromptVersion,
            Model: info.Model,
            Outcome: FormatOutcome(result.Outcome),
            Value: result.IsOk ? result.Value : null,
            Error: result.Error,
            FromCache: info.FromCache,
            Attempts: info.Attempts,
            LatencyMs: (long)info.Latency.TotalMilliseconds,
            InputTokens: usage.InputTokens,
            OutputTokens: usage.OutputTokens,
            ThoughtTokens: usage.ThoughtTokens,
            CachedTokens: usage.CachedTokens,
            CostUsd: info.EstimatedCostUsd,
            RawText: result.IsOk ? null : info.RawText);
    }

    internal static string ConvertPropertyValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Null => "",
            _ => element.GetRawText(),
        };

    private static string? ExtractId(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.String when !string.IsNullOrEmpty(prop.GetString()) => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            _ => null,
        };
    }

    private static string FormatOutcome(AnalysisOutcome outcome) =>
        outcome switch
        {
            AnalysisOutcome.Ok => "ok",
            AnalysisOutcome.Blocked => "blocked",
            AnalysisOutcome.Invalid => "invalid",
            AnalysisOutcome.Truncated => "truncated",
            AnalysisOutcome.Failed => "failed",
            AnalysisOutcome.BudgetExceeded => "budget_exceeded",
            _ => JsonNamingPolicy.SnakeCaseLower.ConvertName(outcome.ToString()),
        };

    private sealed class ModelStatsAccumulator(string? requestedModel)
    {
        private readonly List<long> _latencies = [];
        private string? _resolvedModel = requestedModel;
        private int _items;
        private int _ok;
        private int _invalid;
        private int _blocked;
        private int _truncated;
        private int _failed;
        private int _budgetExceeded;
        private int _cacheHits;
        private long _inputTokens;
        private long _outputTokens;
        private long _thoughtTokens;
        private double _costUsd;

        public void Record(AnalysisResult<JsonElement> result, long latencyMs)
        {
            _resolvedModel ??= result.Info.Model;
            _items++;
            _latencies.Add(latencyMs);

            switch (result.Outcome)
            {
                case AnalysisOutcome.Ok:
                    _ok++;
                    break;
                case AnalysisOutcome.Invalid:
                    _invalid++;
                    break;
                case AnalysisOutcome.Blocked:
                    _blocked++;
                    break;
                case AnalysisOutcome.Truncated:
                    _truncated++;
                    break;
                case AnalysisOutcome.Failed:
                    _failed++;
                    break;
                case AnalysisOutcome.BudgetExceeded:
                    _budgetExceeded++;
                    break;
            }

            if (result.Info.FromCache)
            {
                _cacheHits++;
            }

            var usage = result.Info.Usage;
            _inputTokens += usage.InputTokens;
            _outputTokens += usage.OutputTokens;
            _thoughtTokens += usage.ThoughtTokens;
            _costUsd += result.Info.EstimatedCostUsd;
        }

        public ModelBatchStats ToStats()
        {
            var avgLatency = 0.0;
            var p95Latency = 0.0;
            if (_latencies.Count > 0)
            {
                _latencies.Sort();
                var sum = 0L;
                for (var i = 0; i < _latencies.Count; i++)
                {
                    sum += _latencies[i];
                }

                avgLatency = (double)sum / _latencies.Count;
                var p95Index = Math.Clamp((int)Math.Ceiling(0.95 * _latencies.Count) - 1, 0, _latencies.Count - 1);
                p95Latency = _latencies[p95Index];
            }

            return new ModelBatchStats(
                Model: _resolvedModel ?? "default",
                Items: _items,
                Ok: _ok,
                Invalid: _invalid,
                Blocked: _blocked,
                Truncated: _truncated,
                Failed: _failed,
                BudgetExceeded: _budgetExceeded,
                CacheHits: _cacheHits,
                AvgLatencyMs: avgLatency,
                P95LatencyMs: p95Latency,
                InputTokens: _inputTokens,
                OutputTokens: _outputTokens,
                ThoughtTokens: _thoughtTokens,
                CostUsd: _costUsd);
        }
    }
}
