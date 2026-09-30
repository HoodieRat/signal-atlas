using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SignalAtlas.Core;

namespace SignalAtlas.OpenAI;

/// <summary>OpenAI Responses API using a key supplied by the user's environment.</summary>
public sealed class OpenAiApiBackend : IModelBackend
{
    private readonly HttpClient _http;
    private readonly Func<string?> _keyProvider;
    private string? _model;
    public string? DeferredReason { get; private set; }

    public OpenAiApiBackend(HttpClient? http = null, Func<string?>? keyProvider = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        _keyProvider = keyProvider ?? (() => Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User));
    }

    public Task<bool> PrepareAsync(string modelKey, CancellationToken cancellationToken)
    {
        DeferredReason = null;
        _model = null;
        if (string.IsNullOrWhiteSpace(modelKey)) DeferredReason = "Choose an OpenAI API model";
        else if (string.IsNullOrWhiteSpace(_keyProvider())) DeferredReason = "OPENAI_API_KEY is not set for this Windows user";
        else _model = modelKey.Trim();
        return Task.FromResult(_model is not null);
    }

    public async Task<Analysis?> AnalyzeAsync(Document document, Topic topic, CancellationToken cancellationToken)
    {
        if (_model is null) return null;
        var raw = await RespondAsync(ResearchPrompts.AnalysisInput(document, topic), ResearchPrompts.AnalysisSchema, 1200, cancellationToken);
        return ResearchPrompts.ParseAnalysis(raw, document.Id, "openai-api:" + _model);
    }

    public async Task<ResearchBrief?> SynthesizeAsync(IReadOnlyList<ReportItem> items, CancellationToken cancellationToken)
    {
        var evidence = ReportEvidence.Select(items);
        if (_model is null || evidence.Count == 0) return null;
        var raw = await RespondAsync(ResearchPrompts.SynthesisInput(evidence), ResearchPrompts.SynthesisSchema, 1800, cancellationToken);
        return ResearchPrompts.ParseSynthesis(raw, evidence.Count);
    }

    public Task<string> WriteReportPartAsync(string input, object schema, int maxTokens, CancellationToken cancellationToken)
    {
        if (_model is null) throw new InvalidOperationException("Prepare the AI provider before writing a report.");
        return RespondAsync(input, schema, maxTokens, cancellationToken);
    }

    private async Task<string> RespondAsync(string input, object schema, int maxOutputTokens, CancellationToken token)
    {
        string key = _keyProvider() ?? throw new InvalidOperationException("OPENAI_API_KEY is not set");
        var payload = new
        {
            model = _model,
            instructions = ResearchPrompts.Instructions,
            input,
            store = false,
            max_output_tokens = maxOutputTokens,
            text = new { format = new { type = "json_schema", name = "research_result", strict = true, schema } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await _http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            string message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "OpenAI API key was rejected",
                HttpStatusCode.TooManyRequests => "OpenAI API rate or usage limit reached",
                _ => $"OpenAI API returned HTTP {(int)response.StatusCode}"
            };
            throw new HttpRequestException(message);
        }
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = json.RootElement;
        if (root.GetProperty("status").GetString() != "completed")
            throw new InvalidDataException("OpenAI API response did not complete");
        foreach (var item in root.GetProperty("output").EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) && type.GetString() != "message") continue;
            if (!item.TryGetProperty("content", out var content)) continue;
            foreach (var part in content.EnumerateArray())
                if (part.TryGetProperty("type", out var partType) && partType.GetString() == "output_text"
                    && part.TryGetProperty("text", out var value) && !string.IsNullOrWhiteSpace(value.GetString()))
                    return value.GetString()!;
        }
        throw new InvalidDataException("OpenAI API returned no usable text");
    }

    public Task CleanupAsync(CancellationToken cancellationToken) { _model = null; return Task.CompletedTask; }
}
