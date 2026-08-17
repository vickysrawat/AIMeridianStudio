using MeridianStudio.API.Infrastructure.Retrieval;

namespace MeridianStudio.API.Infrastructure.LLM.Embedding;

/// <summary>Result of domain classification: the chosen vertical, a 0–1 confidence, and the method used.</summary>
public sealed record DomainClassification(string Domain, double Confidence, string Method);

/// <summary>Classifies free text into one of the canonical industry verticals.</summary>
public interface IDomainClassifier
{
    Task<DomainClassification> ClassifyAsync(string text, CancellationToken ct = default);
}

/// <summary>
/// Embedding-similarity domain classifier with a keyword fallback. Replaces brittle first-match
/// keyword detection with a ranked, confidence-scored choice: the input is compared by cosine to
/// a fixed set of vertical descriptors (embedded once and cached). When embeddings are
/// unavailable or weak, it falls back to keyword-hit scoring. Always returns a result — defaulting
/// to "Enterprise AI Platform" when nothing matches — so callers never have to handle nulls.
/// </summary>
public sealed class DomainClassifier(IEmbeddingProvider embedder, ILogger<DomainClassifier> logger)
    : IDomainClassifier
{
    private const string Fallback = "Enterprise AI Platform";
    private const double MinEmbeddingConfidence = 0.18;

    private sealed record Vertical(string Domain, string Descriptor, string[] Keywords);

    private static readonly Vertical[] Verticals =
    [
        new("Healthcare AI",
            "healthcare, clinical, medical, patient, hospital, diagnostics, pharma, EHR, FHIR, biotech",
            ["health", "medical", "clinical", "patient", "hospital", "diagnostic", "pharma", "ehr", "fhir", "nurse", "biotech"]),
        new("Financial Technology",
            "finance, fintech, banking, payments, crypto, trading, investment, insurance, AML, KYC, lending, ledgers",
            ["finance", "fintech", "banking", "payment", "crypto", "trading", "investment", "insurance", "aml", "kyc", "ledger", "bank", "transaction", "stock", "lending"]),
        new("Legal Technology",
            "legal, law, compliance, contracts, attorney, litigation, regulation, eDiscovery, audit",
            ["legal", "law", "compliance", "contract", "attorney", "litigation", "regulation", "discovery", "nda", "audit", "firm"]),
        new("Retail & E-Commerce",
            "retail, e-commerce, shopping, orders, cart, products, delivery, fulfillment, inventory, checkout",
            ["retail", "shop", "store", "order", "cart", "product", "delivery", "fulfillment", "ecommerce", "e-commerce", "inventory", "warehouse", "checkout", "merchant"]),
        new("Real Estate & Property Management",
            "real estate, property management, leasing, tenants, landlords, mortgages, MLS listings",
            ["property", "real estate", "realestate", "home", "leasing", "tenant", "landlord", "rent", "apartment", "mortgage", "listing", "mls"]),
        new("Education & EdTech",
            "education, edtech, learning, schools, teachers, courses, students, curriculum, LMS",
            ["learn", "class", "school", "teacher", "course", "academic", "student", "education", "edtech", "curriculum", "lms"]),
        new("Local Services",
            "local field services, plumbing, HVAC, repairs, contractors, electricians, technician dispatch",
            ["plumbing", "hvac", "repair", "contractor", "electrician", "field service", "technician", "local service", "dispatch"]),
        new("Core Software & Tech",
            "software platforms, DevOps, databases, APIs, cloud, kubernetes, microservices, SaaS, CI/CD",
            ["devops", "database", "api", "cloud", "firewall", "network", "saas", "kubernetes", "microservice", "platform", "cicd", "ci/cd"]),
    ];

    private float[][]? _verticalVectors;   // lazily embedded once; guarded by _gate
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<DomainClassification> ClassifyAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new DomainClassification(Fallback, 0, "default");

        if (embedder.IsRealModel)
        {
            try
            {
                var result = await ClassifyByEmbeddingAsync(text, ct);
                if (result.Confidence >= MinEmbeddingConfidence) return result;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[DomainClassifier] Embedding classification failed — using keyword fallback.");
            }
        }

        return ClassifyByKeyword(text);
    }

    private async Task<DomainClassification> ClassifyByEmbeddingAsync(string text, CancellationToken ct)
    {
        var vectors = await EnsureVerticalVectorsAsync(ct);
        var query   = await embedder.EmbedAsync(text, ct);

        var bestIdx   = -1;
        var bestScore = double.NegativeInfinity;
        for (var i = 0; i < vectors.Length; i++)
        {
            var score = VectorMath.Cosine(query, vectors[i]);
            if (score > bestScore) { bestScore = score; bestIdx = i; }
        }

        return bestIdx >= 0
            ? new DomainClassification(Verticals[bestIdx].Domain, Math.Max(0, bestScore), "embedding")
            : new DomainClassification(Fallback, 0, "default");
    }

    private async Task<float[][]> EnsureVerticalVectorsAsync(CancellationToken ct)
    {
        if (_verticalVectors is not null) return _verticalVectors;

        await _gate.WaitAsync(ct);
        try
        {
            if (_verticalVectors is null)
            {
                var embedded = await embedder.EmbedBatchAsync(
                    [.. Verticals.Select(v => $"{v.Domain}: {v.Descriptor}")], ct);
                _verticalVectors = [.. embedded];
            }
        }
        finally
        {
            _gate.Release();
        }

        return _verticalVectors;
    }

    private static DomainClassification ClassifyByKeyword(string text)
    {
        var hay       = text.ToLowerInvariant();
        var bestIdx   = -1;
        var bestHits  = 0;
        for (var i = 0; i < Verticals.Length; i++)
        {
            var hits = Verticals[i].Keywords.Count(k => hay.Contains(k, StringComparison.Ordinal));
            if (hits > bestHits) { bestHits = hits; bestIdx = i; }
        }

        if (bestIdx < 0) return new DomainClassification(Fallback, 0, "default");

        // Confidence scales with how many distinct keywords matched (capped).
        var confidence = Math.Min(1.0, bestHits / 3.0);
        return new DomainClassification(Verticals[bestIdx].Domain, confidence, "keyword");
    }
}
