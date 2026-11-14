using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Junction.Web.Data;
using Junction.Web.Features.Analysis;
using Junction.Web.Features.Analysis.Batch;
using Junction.Web.Features.Analysis.Prompts;
using Junction.Web.Features.Analysis.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Junction.Analyze;

public static class AnalyzeCli
{
    private static readonly JsonSerializerOptions JsonOptions = new(AnalysisJson.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        ParsedArgs parsed;
        try
        {
            parsed = ParseArgs(args);
        }
        catch (UsageException ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            await Console.Error.WriteLineAsync();
            await WriteUsageAsync(Console.Error);
            return 2;
        }

        if (parsed.ShowHelp)
        {
            await WriteUsageAsync(Console.Error);
            return 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        IHost? host = null;
        try
        {
            host = BuildHost(parsed.DbPath);

            var dbFactory = host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using (var db = await dbFactory.CreateDbContextAsync(cts.Token))
            {
                await db.Database.MigrateAsync(cts.Token);
            }

            return parsed.Command switch
            {
                PromptsCommand => ExecutePrompts(host.Services),
                RunCommand runCmd => await ExecuteRunAsync(host.Services, runCmd, cts.Token),
                BatchCommand batchCmd => await ExecuteBatchAsync(host.Services, batchCmd, cts.Token),
                UsageCommand usageCmd => await ExecuteUsageAsync(host.Services, usageCmd, cts.Token),
                CacheClearCommand clearCmd => await ExecuteCacheClearAsync(host.Services, clearCmd, cts.Token),
                _ => 2,
            };
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host?.Dispose();
            }
        }
    }

    private static IHost BuildHost(string? dbOverride)
    {
        var repoRoot = FindRepoRoot();
        var appSettingsPath = Path.Combine(repoRoot, "src", "Junction.Web", "appsettings.json");
        var dbPath = dbOverride is not null
            ? Path.GetFullPath(dbOverride)
            : Path.Combine(repoRoot, "src", "Junction.Web", "app.db");

        if (Path.GetDirectoryName(dbPath) is { Length: > 0 } dbDir)
        {
            Directory.CreateDirectory(dbDir);
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile(appSettingsPath, optional: false, reloadOnChange: false)
            .AddUserSecrets(typeof(AnalysisFeature).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:App"] = $"Data Source={dbPath}",
            });

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter((string?)null, LogLevel.Warning);
        builder.Logging.AddFilter("Junction", LogLevel.Information);
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAppData();
        builder.Services.AddAnalysisFeature();
        builder.Services.AddSingleton<AnalysisBatchRunner>();

        return builder.Build();
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            var dir = new DirectoryInfo(Path.GetFullPath(start));
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SomeMysteriousAppForJunction.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not find repository root containing SomeMysteriousAppForJunction.sln.");
    }

    private static int ExecutePrompts(IServiceProvider services)
    {
        var prompts = services.GetRequiredService<PromptLibrary>();
        var geminiOptions = services.GetRequiredService<IOptions<GeminiOptions>>().Value;

        var list = prompts.All
            .Select(p => new PromptInfoOutput(
                p.Id,
                p.FullVersion,
                p.Job,
                geminiOptions.ModelFor(p.Job),
                p.Variables,
                p.Source))
            .ToArray();

        Console.Out.WriteLine(JsonSerializer.Serialize(list, JsonOptions));
        return 0;
    }

    private static async Task<int> ExecuteRunAsync(IServiceProvider services, RunCommand cmd, CancellationToken ct)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var varsSource in cmd.VarsSources)
        {
            var json = varsSource == "-"
                ? await Console.In.ReadToEndAsync(ct)
                : await File.ReadAllTextAsync(varsSource, Encoding.UTF8, ct);
            ReadVariablesJson(json, variables);
        }

        foreach (var (name, value) in cmd.InlineVars)
        {
            variables[name] = value;
        }

        var runner = services.GetRequiredService<AnalysisBatchRunner>();
        BatchResultLine? singleLine = null;
        await runner.RunAsync(
            [new BatchItem("run", variables)],
            new BatchOptions(cmd.PromptId, [cmd.Model])
            {
                Scope = cmd.Scope,
                Concurrency = 1,
                Cache = cmd.Cache,
            },
            line =>
            {
                singleLine = line;
                return Task.CompletedTask;
            },
            skip: null,
            ct: ct);

        if (singleLine is null)
        {
            throw new InvalidOperationException("Batch runner completed without producing a result.");
        }

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(singleLine, JsonOptions));
        return 0;
    }

    private static async Task<int> ExecuteBatchAsync(IServiceProvider services, BatchCommand cmd, CancellationToken ct)
    {
        IReadOnlyList<BatchItem> items;
        using (var reader = new StreamReader(cmd.InputPath, Encoding.UTF8))
        {
            items = AnalysisBatchRunner.ParseJsonl(reader);
        }

        if (cmd.Limit is { } limit && items.Count > limit)
        {
            items = items.Take(limit).ToList();
        }

        IReadOnlyList<string?> models;
        if (cmd.Models.Count > 0)
        {
            models = cmd.Models;
        }
        else
        {
            var promptLibrary = services.GetRequiredService<PromptLibrary>();
            var geminiOptions = services.GetRequiredService<IOptions<GeminiOptions>>().Value;
            var definition = promptLibrary.Get(cmd.PromptId);
            models = [geminiOptions.ModelFor(definition.Job)];
        }

        var outFullPath = Path.GetFullPath(cmd.OutputPath);
        if (Path.GetDirectoryName(outFullPath) is { Length: > 0 } outDir)
        {
            Directory.CreateDirectory(outDir);
        }

        ISet<(string Id, string Model)>? skip = null;
        if (File.Exists(outFullPath))
        {
            using var existingReader = new StreamReader(outFullPath, Encoding.UTF8);
            skip = AnalysisBatchRunner.ReadCompleted(existingReader);
        }

        var runner = services.GetRequiredService<AnalysisBatchRunner>();
        var batchOptions = new BatchOptions(cmd.PromptId, models)
        {
            Scope = cmd.Scope,
            Concurrency = cmd.Concurrency,
            Cache = cmd.Cache,
        };

        IReadOnlyList<ModelBatchStats> stats;
        await using (var stream = new FileStream(outFullPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            stats = await runner.RunAsync(
                items,
                batchOptions,
                async line =>
                {
                    var json = JsonSerializer.Serialize(line, JsonOptions);
                    await writer.WriteLineAsync(json);
                    await writer.FlushAsync(CancellationToken.None);
                },
                skip: skip,
                ct: ct);
        }

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(stats, JsonOptions));
        return 0;
    }

    private static async Task<int> ExecuteUsageAsync(IServiceProvider services, UsageCommand cmd, CancellationToken ct)
    {
        var time = services.GetRequiredService<TimeProvider>();
        var since = cmd.Since ?? new DateTimeOffset(time.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);

        var ledger = services.GetRequiredService<AnalysisLedger>();
        var summary = await ledger.SummarizeAsync(since, cmd.Scope, ct);
        var byModel = await ledger.ByModelAsync(since, ct);

        var output = new UsageOutput(summary, byModel);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(output, JsonOptions));
        return 0;
    }

    private static async Task<int> ExecuteCacheClearAsync(
        IServiceProvider services,
        CacheClearCommand cmd,
        CancellationToken ct)
    {
        var cache = services.GetRequiredService<AnalysisCache>();
        var deleted = await cache.ClearAsync(cmd.PromptId, ct);

        var output = new CacheClearOutput(deleted);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(output, JsonOptions));
        return 0;
    }

    private static void ReadVariablesJson(string json, Dictionary<string, string> target)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Expected a JSON object for --vars, got {doc.RootElement.ValueKind}.");
        }

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            target[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString()!,
                JsonValueKind.Null => "",
                _ => prop.Value.GetRawText(),
            };
        }
    }

    private static ParsedArgs ParseArgs(string[] args)
    {
        if (args.Length == 0)
        {
            throw new UsageException("Missing command.");
        }

        string? dbPath = null;
        var filtered = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                return new ParsedArgs(ShowHelp: true, DbPath: null, Command: null);
            }

            if (arg == "--db")
            {
                dbPath = NextValue(args, ref i, "--db");
                continue;
            }

            filtered.Add(arg);
        }

        if (filtered.Count == 0)
        {
            throw new UsageException("Missing command.");
        }

        var commandName = filtered[0];
        var rest = filtered.Skip(1).ToArray();

        CliCommand command = commandName switch
        {
            "prompts" => ParsePrompts(rest),
            "run" => ParseRun(rest),
            "batch" => ParseBatch(rest),
            "usage" => ParseUsage(rest),
            "cache-clear" => ParseCacheClear(rest),
            _ => throw new UsageException($"Unknown command '{commandName}'."),
        };

        return new ParsedArgs(ShowHelp: false, DbPath: dbPath, Command: command);
    }

    private static PromptsCommand ParsePrompts(string[] args)
    {
        if (args.Length > 0)
        {
            throw new UsageException($"Unexpected argument '{args[0]}' for 'prompts'.");
        }

        return new PromptsCommand();
    }

    private static RunCommand ParseRun(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            throw new UsageException("'run' requires <prompt-id>.");
        }

        var promptId = args[0];
        var varsSources = new List<string>();
        var inlineVars = new List<(string Name, string Value)>();
        string? model = null;
        string? scope = null;
        var noCache = false;
        var refresh = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--vars":
                    varsSources.Add(NextValue(args, ref i, "--vars"));
                    break;
                case "--var":
                {
                    var pair = NextValue(args, ref i, "--var");
                    var eq = pair.IndexOf('=');
                    if (eq <= 0)
                    {
                        throw new UsageException($"Invalid --var '{pair}'; expected name=value.");
                    }

                    inlineVars.Add((pair[..eq], pair[(eq + 1)..]));
                    break;
                }
                case "--model":
                    model = NextValue(args, ref i, "--model");
                    break;
                case "--scope":
                    scope = NextValue(args, ref i, "--scope");
                    break;
                case "--no-cache":
                    noCache = true;
                    break;
                case "--refresh":
                    refresh = true;
                    break;
                default:
                    throw new UsageException($"Unknown argument '{arg}' for 'run'.");
            }
        }

        return new RunCommand(promptId, varsSources, inlineVars, model, scope, ResolveCachePolicy(noCache, refresh));
    }

    private static BatchCommand ParseBatch(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            throw new UsageException("'batch' requires <prompt-id>.");
        }

        var promptId = args[0];
        string? inputPath = null;
        string? outputPath = null;
        var models = new List<string>();
        string? scope = null;
        var concurrency = 4;
        int? limit = null;
        var noCache = false;
        var refresh = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--in":
                    inputPath = NextValue(args, ref i, "--in");
                    break;
                case "--out":
                    outputPath = NextValue(args, ref i, "--out");
                    break;
                case "--model":
                {
                    var raw = NextValue(args, ref i, "--model");
                    var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length == 0)
                    {
                        throw new UsageException("--model requires at least one model id.");
                    }

                    foreach (var p in parts)
                    {
                        if (!models.Contains(p, StringComparer.Ordinal))
                        {
                            models.Add(p);
                        }
                    }

                    break;
                }
                case "--scope":
                    scope = NextValue(args, ref i, "--scope");
                    break;
                case "--concurrency":
                {
                    var raw = NextValue(args, ref i, "--concurrency");
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out concurrency) || concurrency < 1)
                    {
                        throw new UsageException($"Invalid --concurrency '{raw}'; expected an integer >= 1.");
                    }

                    break;
                }
                case "--limit":
                {
                    var raw = NextValue(args, ref i, "--limit");
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit) || parsedLimit < 1)
                    {
                        throw new UsageException($"Invalid --limit '{raw}'; expected an integer >= 1.");
                    }

                    limit = parsedLimit;
                    break;
                }
                case "--no-cache":
                    noCache = true;
                    break;
                case "--refresh":
                    refresh = true;
                    break;
                default:
                    throw new UsageException($"Unknown argument '{arg}' for 'batch'.");
            }
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new UsageException("'batch' requires --in <items.jsonl>.");
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new UsageException("'batch' requires --out <results.jsonl>.");
        }

        return new BatchCommand(
            promptId,
            inputPath,
            outputPath,
            models,
            scope,
            concurrency,
            limit,
            ResolveCachePolicy(noCache, refresh));
    }

    private static UsageCommand ParseUsage(string[] args)
    {
        DateTimeOffset? since = null;
        string? scope = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--since":
                {
                    var raw = NextValue(args, ref i, "--since");
                    if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    {
                        throw new UsageException($"Invalid --since '{raw}'; expected yyyy-MM-dd.");
                    }

                    since = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);
                    break;
                }
                case "--scope":
                    scope = NextValue(args, ref i, "--scope");
                    break;
                default:
                    throw new UsageException($"Unknown argument '{arg}' for 'usage'.");
            }
        }

        return new UsageCommand(since, scope);
    }

    private static CacheClearCommand ParseCacheClear(string[] args)
    {
        string? promptId = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--prompt":
                    promptId = NextValue(args, ref i, "--prompt");
                    break;
                default:
                    throw new UsageException($"Unknown argument '{arg}' for 'cache-clear'.");
            }
        }

        return new CacheClearCommand(promptId);
    }

    private static CachePolicy ResolveCachePolicy(bool noCache, bool refresh) =>
        (noCache, refresh) switch
        {
            (true, true) => throw new UsageException("Cannot combine --no-cache and --refresh."),
            (true, false) => CachePolicy.Bypass,
            (false, true) => CachePolicy.Refresh,
            (false, false) => CachePolicy.ReadWrite,
        };

    private static string NextValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new UsageException($"Missing value for {optionName}.");
        }

        index++;
        return args[index];
    }

    private static Task WriteUsageAsync(TextWriter writer) =>
        writer.WriteLineAsync(
            """
            Usage: Junction.Analyze [--db <path>] <command> [options]

            Commands:
              prompts
                List available prompts and their configured models as JSON.

              run <prompt-id> [--vars <file.json>|-] [--var name=value]... [--model m] [--scope s] [--no-cache|--refresh]
                Run a single prompt and write one BatchResultLine JSON object to stdout.

              batch <prompt-id> --in <items.jsonl> --out <results.jsonl> [--model m1,m2,...] [--scope s] [--concurrency n] [--limit n] [--no-cache|--refresh]
                Run a JSONL batch (resuming any completed (id, model) pairs in --out; pass --model explicitly when resuming model overrides) and write ModelBatchStats[] JSON to stdout.

              usage [--since yyyy-MM-dd] [--scope s]
                Print usage summary and per-model breakdown since UTC date (default: UTC today).

              cache-clear [--prompt id]
                Clear all cached responses, or only entries for a given prompt id.
            """);

    private sealed class UsageException(string message) : Exception(message);

    private sealed record ParsedArgs(bool ShowHelp, string? DbPath, CliCommand? Command);

    private abstract record CliCommand;

    private sealed record PromptsCommand : CliCommand;

    private sealed record RunCommand(
        string PromptId,
        IReadOnlyList<string> VarsSources,
        IReadOnlyList<(string Name, string Value)> InlineVars,
        string? Model,
        string? Scope,
        CachePolicy Cache) : CliCommand;

    private sealed record BatchCommand(
        string PromptId,
        string InputPath,
        string OutputPath,
        IReadOnlyList<string> Models,
        string? Scope,
        int Concurrency,
        int? Limit,
        CachePolicy Cache) : CliCommand;

    private sealed record UsageCommand(DateTimeOffset? Since, string? Scope) : CliCommand;

    private sealed record CacheClearCommand(string? PromptId) : CliCommand;

    private sealed record PromptInfoOutput(
        string Id,
        string Version,
        AnalysisJob Job,
        string Model,
        IReadOnlyList<string> Variables,
        string Source);

    private sealed record UsageOutput(UsageSummary Summary, IReadOnlyList<ModelUsage> ByModel);

    private sealed record CacheClearOutput(int Deleted);
}
