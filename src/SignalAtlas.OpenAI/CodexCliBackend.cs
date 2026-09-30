using System.Diagnostics;
using System.Text.Json;
using SignalAtlas.Collectors;
using SignalAtlas.Core;

namespace SignalAtlas.OpenAI;

/// <summary>Runs the user's installed Codex CLI with their existing ChatGPT sign-in.</summary>
public sealed class CodexCliBackend : IModelBackend
{
    private readonly Func<string[], string?, CancellationToken, Task<(int ExitCode, string Output)>> _run;
    private readonly bool _usingInstalled;
    private bool _ready;
    public string? DeferredReason { get; private set; }

    public CodexCliBackend(Func<string[], string?, CancellationToken, Task<(int ExitCode, string Output)>>? run = null)
    {
        _usingInstalled = run is null;
        _run = run ?? RunInstalledAsync;
    }

    public async Task<bool> PrepareAsync(string modelKey, CancellationToken cancellationToken)
    {
        _ready = false;
        DeferredReason = null;
        if (_usingInstalled && FindCommand() is null)
        {
            DeferredReason = "Codex CLI is not installed";
            return false;
        }
        try
        {
            // Some desktop Codex settings (for example service_tier="default") are
            // newer than an installed CLI. Override that setting for this sign-in
            // check; the analysis command below already ignores user config.
            string[] statusArgs = _usingInstalled
                ? ["-c", "service_tier=\"flex\"", "login", "status"]
                : ["login", "status"];
            var result = await _run(statusArgs, null, cancellationToken);
            if (result.ExitCode != 0 || !result.Output.Contains("using ChatGPT", StringComparison.OrdinalIgnoreCase))
            {
                DeferredReason = "Sign in to Codex CLI with a ChatGPT account first";
                return false;
            }
            _ready = true;
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            DeferredReason = "Codex CLI sign-in check failed";
            return false;
        }
    }

    public async Task<Analysis?> AnalyzeAsync(Document document, Topic topic, CancellationToken cancellationToken)
    {
        if (!_ready) return null;
        string raw = await CompleteAsync(ResearchPrompts.AnalysisInput(document, topic), ResearchPrompts.AnalysisSchema, cancellationToken);
        return ResearchPrompts.ParseAnalysis(raw, document.Id, "codex-chatgpt");
    }

    public async Task<ResearchBrief?> SynthesizeAsync(IReadOnlyList<ReportItem> items, CancellationToken cancellationToken)
    {
        var evidence = ReportEvidence.Select(items);
        if (!_ready || evidence.Count == 0) return null;
        string raw = await CompleteAsync(ResearchPrompts.SynthesisInput(evidence), ResearchPrompts.SynthesisSchema, cancellationToken);
        return ResearchPrompts.ParseSynthesis(raw, evidence.Count);
    }

    public Task<string> WriteReportPartAsync(string input, object schema, int maxTokens, CancellationToken cancellationToken)
    {
        if (!_ready) throw new InvalidOperationException("Prepare the AI provider before writing a report.");
        return CompleteAsync(input, schema, cancellationToken);
    }

    private async Task<string> CompleteAsync(string input, object schema, CancellationToken token)
    {
        string temp = Path.Combine(AppPaths.State, "codex-temp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string schemaPath = Path.Combine(temp, "schema.json");
        string outputPath = Path.Combine(temp, "result.json");
        try
        {
            File.WriteAllText(schemaPath, JsonSerializer.Serialize(schema));
            string[] args = ["exec", "--ephemeral", "--ignore-user-config", "--ignore-rules",
                "-c", "shell_environment_policy.inherit=none",
                "--sandbox", "read-only", "--skip-git-repo-check", "-C", temp,
                "--output-schema", schemaPath, "--output-last-message", outputPath, "-"];
            string prompt = ResearchPrompts.Instructions +
                "\nDo not use tools, browse, or inspect local files. Return only a JSON object matching the supplied schema.\n\n" + input;
            var result = await _run(args, prompt, token);
            if (result.ExitCode != 0) throw new InvalidOperationException("Codex CLI could not complete the analysis. Check its ChatGPT sign-in and usage limits.");
            string raw = File.Exists(outputPath) ? File.ReadAllText(outputPath) : result.Output;
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidDataException("Codex CLI returned no analysis");
            return raw;
        }
        finally
        {
            try { File.Delete(schemaPath); File.Delete(outputPath); Directory.Delete(temp); } catch { }
        }
    }

    public Task CleanupAsync(CancellationToken cancellationToken) { _ready = false; return Task.CompletedTask; }

    public static bool IsInstalled => FindCommand() is not null;

    private static (string File, string[] Prefix)? FindCommand()
    {
        string? executable = ProcessTool.FindExecutable("codex");
        if (executable is not null) return (executable, []);
        string? node = ProcessTool.FindExecutable("node");
        string script = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "@openai", "codex", "bin", "codex.js");
        return node is not null && File.Exists(script) ? (node, [script]) : null;
    }

    private static async Task<(int ExitCode, string Output)> RunInstalledAsync(string[] args, string? input, CancellationToken token)
    {
        var command = FindCommand() ?? throw new FileNotFoundException("Codex CLI is not installed");
        var psi = new ProcessStartInfo(command.File)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in command.Prefix.Concat(args)) psi.ArgumentList.Add(arg);
        // A user-selected API credential must not silently replace ChatGPT sign-in.
        foreach (string key in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN",
            "OPENAI_FEDERATION_RULE_ID", "OPENAI_IDENTITY_TOKEN_FILE" }) psi.Environment.Remove(key);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Codex CLI");
        if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), token);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch { } throw; }
        string output = await stdout;
        string error = await stderr;
        // `codex login status` prints its status on stderr, including on success.
        // Keep analysis diagnostics private; only the sign-in probe reads stderr.
        return (process.ExitCode, args.Contains("login") && args.Contains("status") ? error : output);
    }
}
