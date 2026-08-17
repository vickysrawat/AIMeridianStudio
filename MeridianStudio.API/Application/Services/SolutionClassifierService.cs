using System.Text;
using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Application.Services;

/// <summary>
/// Keyword-heuristic classifier that detects the solution type from a blueprint's
/// text fields. No LLM call — runs in microseconds, always available.
/// Result is used as grounding context in document and mission-suggestion prompts.
/// </summary>
public sealed class SolutionClassifierService
{
    private static readonly (string SolutionType, string[] Keywords)[] _groups =
    [
        ("Azure Serverless",  ["function app", "azure function", "durable function", "logic app",
                               "event grid trigger", "service bus trigger", "consumption plan",
                               "azure function app", "functionapp"]),
        ("Console App",       ["console app", "console application", "cli tool", "command-line",
                               "background worker", "hosted service", "cron job",
                               "scheduled task", "worker service"]),
        ("Batch Processing",  ["batch processing", "batch job", "bulk processing", "offline processing",
                               "corpus", "indexing pipeline", "reindex", "crawl", "backfill",
                               "nightly job", "map-reduce", "bulk ingest"]),
        ("Data Pipeline",     ["etl", "data pipeline", "data ingestion", "data lake",
                               "data warehouse", "spark", "dbt", "databricks", "synapse",
                               "data flow"]),
        ("Streaming / Real-Time", ["real-time streaming", "websocket", "server-sent events",
                               "kafka streams", "flink", "signalr", "live updates",
                               "stream ingestion", "low-latency stream"]),
        ("Event-Driven",      ["event sourcing", "cqrs", "kafka", "rabbitmq", "pub/sub",
                               "message bus", "event bus", "dead letter", "outbox pattern",
                               "saga pattern", "domain event"]),
        ("ML Inference",      ["inference", "model serving", "onnx", "ml pipeline",
                               "fine-tuning", "model deployment", "feature store", "mlops",
                               "training pipeline"]),
        ("RAG / Knowledge Retrieval", ["retrieval-augmented", "rag", "vector database", "vector store",
                               "semantic search", "knowledge base", "embeddings", "chunking",
                               "reranking", "retrieval pipeline"]),
        ("Agentic AI",        ["ai agent", "multi-agent", "agentic", "tool calling", "autonomous agent",
                               "agent workflow", "llm agent", "react agent", "tool-use", "planner-executor"]),
        ("Microservices",     ["service mesh", "kubernetes", "sidecar", "istio",
                               "api gateway + service", "service discovery", "multiple services",
                               "distributed service", "microservice architecture"]),
        ("Monolith",          ["monolith", "modular monolith", "single deployable", "layered monolith",
                               "n-tier architecture", "single codebase deployment"]),
        ("Web App",           ["spa", "single page app", "razor pages", "blazor",
                               "react frontend", "angular frontend", "server-side rendering",
                               "mvc application", "web application"]),
        ("Static Site",       ["static site", "jamstack", "static site generator", "gatsby",
                               "hugo", "eleventy", "prerendered", "cdn-hosted static", "ssg"]),
        ("Mobile App",        ["ios app", "android app", "react native", "flutter", "swiftui",
                               "jetpack compose", "kotlin multiplatform", "mobile app", "maui"]),
        ("Desktop App",       ["desktop app", "electron", "wpf", "winforms", "tauri",
                               "native desktop", "avalonia"]),
        ("REST API",          ["rest api", "http endpoint", "openapi", "swagger",
                               "rest endpoint", "web api", "controller", "minimal api"]),
        ("GraphQL API",       ["graphql", "apollo server", "graphql schema", "resolvers",
                               "schema federation", "graphql gateway"]),
    ];

    private static readonly string[] _knownTypes = [.. _groups.Select(g => g.SolutionType)];

    /// <summary>All solution-type labels the classifier recognises — the canonical vocabulary
    /// the generation LLM is told to choose from, and the set used to validate its answer.</summary>
    public IReadOnlyList<string> KnownTypes => _knownTypes;

    // Common synonyms / shorthands an LLM (or caller) might emit, mapped to the canonical label.
    private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["serverless"] = "Azure Serverless", ["faas"] = "Azure Serverless",
        ["console"] = "Console App", ["cli"] = "Console App", ["worker"] = "Console App",
        ["batch"] = "Batch Processing",
        ["etl"] = "Data Pipeline",
        ["streaming"] = "Streaming / Real-Time", ["real-time"] = "Streaming / Real-Time", ["realtime"] = "Streaming / Real-Time",
        ["event driven"] = "Event-Driven", ["eda"] = "Event-Driven", ["event-based"] = "Event-Driven",
        ["inference"] = "ML Inference", ["model serving"] = "ML Inference",
        ["rag"] = "RAG / Knowledge Retrieval", ["retrieval"] = "RAG / Knowledge Retrieval",
        ["agent"] = "Agentic AI", ["agentic"] = "Agentic AI", ["multi-agent"] = "Agentic AI",
        ["microservice"] = "Microservices", ["micro-service"] = "Microservices",
        ["monolith"] = "Monolith", ["monolithic"] = "Monolith",
        ["web app"] = "Web App", ["web application"] = "Web App", ["spa"] = "Web App", ["frontend"] = "Web App",
        ["static site"] = "Static Site", ["jamstack"] = "Static Site", ["ssg"] = "Static Site",
        ["mobile"] = "Mobile App",
        ["desktop"] = "Desktop App",
        ["graphql"] = "GraphQL API",
        ["rest"] = "REST API", ["web api"] = "REST API",
    };

    /// <summary>
    /// Map a candidate label (e.g. the LLM-supplied <c>solutionType</c>) to the canonical known label,
    /// tolerating case and common synonyms. Returns null when nothing plausibly matches so the caller
    /// falls back to the keyword heuristic. Lets the generation LLM's own classification (server-side →
    /// not client-spoofable) drive the type, with the heuristic only as a safety net.
    /// </summary>
    public string? Canonicalize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        var c = candidate.Trim();

        var exact = _knownTypes.FirstOrDefault(t => string.Equals(t, c, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        if (_aliases.TryGetValue(c, out var mapped)) return mapped;

        var lc = c.ToLowerInvariant();
        foreach (var (key, value) in _aliases)
            if (lc.Contains(key, StringComparison.Ordinal)) return value;

        return null;
    }

    public (string SolutionType, double Confidence) Classify(
        string baseTopology, string coreScenario, string endpointManifest)
        => ClassifyCorpus(string.Concat(
            baseTopology.ToLowerInvariant(), " ",
            coreScenario.ToLowerInvariant(), " ",
            endpointManifest.ToLowerInvariant()));

    /// <summary>
    /// Classify over the fuller design. In addition to the three text fields, the corpus includes the
    /// keyword-dense design fields — tech radar, arch decisions, project notes, buy-vs-build, quality
    /// attributes — so the confidence tracks what the user actually edits (the tech radar/decisions are
    /// the strongest type signal). Confidence formula is unchanged; richer input simply yields more hits.
    /// </summary>
    public (string SolutionType, double Confidence) Classify(SystemBlueprint bp)
        => ClassifyCorpus(BuildCorpus(bp));

    /// <summary>Lowercased corpus of every field that carries solution-type signal.</summary>
    private static string BuildCorpus(SystemBlueprint bp)
    {
        var sb = new StringBuilder();
        sb.Append(bp.BaseTopology).Append(' ')
          .Append(bp.CoreScenario).Append(' ')
          .Append(bp.EndpointManifest).Append(' ')
          .Append(bp.ProjectNotes).Append(' ');

        foreach (var t in bp.TechRadar)
        {
            sb.Append(t.Layer).Append(' ');
            if (t.Technologies is { Length: > 0 })
                sb.Append(string.Join(' ', t.Technologies)).Append(' ');
        }

        foreach (var d in bp.ArchDecisions)
        {
            sb.Append(d.Decision).Append(' ').Append(d.ChosenApproach).Append(' ').Append(d.Rationale).Append(' ');
            if (d.AlternativesConsidered is { Length: > 0 })
                sb.Append(string.Join(' ', d.AlternativesConsidered)).Append(' ');
        }

        foreach (var b in bp.BuyVsBuild)
            sb.Append(b.Component).Append(' ').Append(b.BuyOption).Append(' ')
              .Append(b.BuildApproach).Append(' ').Append(b.Recommendation).Append(' ');

        foreach (var q in bp.QualityAttributes)
            sb.Append(q.Attribute).Append(' ').Append(q.Target).Append(' ');

        return sb.ToString().ToLowerInvariant();
    }

    private static (string SolutionType, double Confidence) ClassifyCorpus(string text)
    {
        var best = ("REST API", 0.0);

        foreach (var (solutionType, keywords) in _groups)
        {
            var hits = keywords.Count(kw => text.Contains(kw, StringComparison.Ordinal));
            if (hits == 0) continue;

            var confidence = Math.Min(0.95, 0.40 + (hits / (double)keywords.Length) * 0.55);
            if (confidence > best.Item2)
                best = (solutionType, confidence);
        }

        return best;
    }
}
