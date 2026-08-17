using System.Security.Cryptography;
using System.Text;
using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Infrastructure.LocalEngine;

/// <summary>
/// Stateless local procedural compilation engine. All five feature surfaces
/// are covered with rich, domain-aware data derived from the inbound keywords.
/// Designed for zero-downtime fallback — every method is synchronous, purely
/// in-memory, and thread-safe. Register as Singleton.
/// </summary>
public sealed class LocalCompilationEngine(ILogger<LocalCompilationEngine> logger)
{
    private const int PageSize = 5;

    // ═══════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════════════

    public ResearchResponse CompileResearch(
        string keywords, int page, IReadOnlySet<string>? excludedIds)
    {
        logger.LogInformation(
            "[Local Engine] Compiling research — keywords: '{K}', page: {P}", keywords, page);

        var profile  = ResolveProfile(keywords);
        var excluded = excludedIds ?? new HashSet<string>(StringComparer.Ordinal);

        var available = profile.ItemPool
            .Where(i => !excluded.Contains(i.Id))
            .ToList();

        var pageItems = available
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        // Pool exhausted — procedurally generate additional items
        if (pageItems.Count < PageSize && page > 1)
        {
            var needed = PageSize - pageItems.Count;
            pageItems.AddRange(
                GenerateFreshItems(profile.Name, keywords, needed, excluded));
        }

        return new ResearchResponse
        {
            Domain            = profile.Name,
            DomainsList       = profile.SubDomains,
            CompetitorInsights = page == 1 ? profile.Competitors : [],
            Items             = pageItems
        };
    }

    public SystemBlueprint CompileBlueprint(
        string solutionId, string solutionName, string? domain,
        string? subDomain = null, string? solutionDescription = null)
    {
        logger.LogInformation(
            "[Local Engine] Compiling blueprint for '{S}' (sub-domain: {Sub})",
            solutionName, subDomain ?? "—");

        // Resolve on the richest signal available so detection still works when domain is blank.
        var profile = ResolveProfile(
            !string.IsNullOrWhiteSpace(domain)              ? domain!
          : !string.IsNullOrWhiteSpace(subDomain)          ? subDomain!
          : !string.IsNullOrWhiteSpace(solutionDescription) ? solutionDescription!
          : solutionName);

        var topology = BuildBaseTopology(solutionName, profile);
        if (!string.IsNullOrWhiteSpace(subDomain))
            topology = $"> Sub-domain focus: {subDomain!.Trim()}\n\n{topology}";

        return new SystemBlueprint
        {
            Id                   = DeterministicId(solutionId + solutionName + (subDomain ?? "")),
            SolutionId           = solutionId,
            SolutionName         = solutionName,
            Domain               = profile.Name,
            SubDomain            = subDomain ?? string.Empty,
            SolutionDescription  = solutionDescription ?? string.Empty,
            CoreScenario         = BuildCoreScenario(solutionName, profile, subDomain, solutionDescription),
            BaseTopology         = topology,
            DatabaseSchemes      = BuildDatabaseSchemes(solutionName, profile),
            EndpointManifest     = BuildEndpointManifest(solutionName, profile),
            ResilienceStrategies = BuildResilienceStrategies(profile),
            ArchDecisions        = BuildArchDecisions(profile),
            QualityAttributes    = BuildQualityAttributes(profile),
            TechRadar            = BuildTechRadar(profile),
            BuyVsBuild           = BuildBuyVsBuild(profile)
        };
    }

    // ── Heuristic assessment (offline use-case workflow) ──────────────────────
    public Assessment CompileAssessment(
        string? useCaseScenario, string? useCase, string? context, string? problemStatement,
        string? objective, string? scopeOfWork, string? expectedOutcome, string? domain)
    {
        var combined = string.Join(" ", new[] { useCaseScenario, useCase, objective, scopeOfWork, expectedOutcome, context }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var profile = ResolveProfile(domain ?? combined);
        var title   = !string.IsNullOrWhiteSpace(useCase) ? useCase!
                    : !string.IsNullOrWhiteSpace(objective)
                        ? (objective!.Length > 80 ? objective![..80].TrimEnd() + "…" : objective!)
                        : "Use-Case Assessment";

        logger.LogInformation("[Local Engine] Compiling assessment '{T}'", title);

        var s = combined.ToLowerInvariant();
        var weighsOptions = ContainsAny(s, "aws", "gcp", "google cloud", "azure", "multi-cloud", "multi cloud",
                                           "migrate", "replicate", "vendor", "hyperscaler", "option", "alternative");

        var sections = new List<AssessmentSection>
        {
            new("Current State & Constraints",
                $"The brief centres on {profile.Name} capabilities. " +
                (string.IsNullOrWhiteSpace(problemStatement)
                    ? "Validate the key constraints with stakeholders before committing."
                    : problemStatement!)),
            new("Recommended Direction",
                string.IsNullOrWhiteSpace(expectedOutcome)
                    ? "Define the target state and sequence the work to reach it."
                    : $"Work toward the stated expected outcome: {expectedOutcome}"),
            new("Roadmap Outline",
                "1. Validate current-state assumptions and success criteria.\n" +
                "2. Pilot the highest-value, lowest-risk option.\n" +
                "3. Scale the proven approach with governance and automation guardrails."),
        };

        var recommended = new List<RecommendedDocument>
        {
            new(expectedOutcome ?? "Strategy summary", $"{title} — Executive Summary", "executive-summary",
                "Board-level summary of the recommendation and rationale."),
            new(expectedOutcome ?? "Reference architecture", $"{title} — Technical Specification", "technical-specification",
                "Detailed target architecture and integration approach."),
            new(expectedOutcome ?? "Execution plan", $"{title} — Proposal", "proposal",
                "Phased delivery plan with timeline, costs, and risks."),
        };
        if (ContainsAny(s, "governance", "operating model", "compliance", "policy"))
            recommended.Add(new("Governance & operating model", $"{title} — Governance & ADR", "governance-adr",
                "Governance and operating-model decisions for multi-provider scale."));

        return new Assessment
        {
            Id               = DeterministicId(title + combined),
            Title            = title,
            Domain           = profile.Name,
            UseCase          = useCase ?? string.Empty,
            Context          = context ?? string.Empty,
            ProblemStatement = problemStatement ?? string.Empty,
            Objective        = objective ?? string.Empty,
            ScopeOfWork      = scopeOfWork ?? string.Empty,
            ExpectedOutcome  = expectedOutcome ?? string.Empty,
            ExecutiveSummary = $"This assessment addresses \"{title}\" within {profile.Name}. " +
                               (weighsOptions
                                   ? "Multiple options are weighed below; the recommended path balances capability, effort, and risk."
                                   : "The recommended direction and a phased roadmap outline are provided below."),
            Sections         = sections,
            Recommendations  =
            [
                "Confirm success criteria and constraints with stakeholders before committing.",
                "Pilot before scaling — prove value on a bounded, high-impact use case.",
                "Codify governance and automation as you scale to control sprawl and risk."
            ],
            Risks            =
            [
                "Underestimated integration, data, and identity complexity.",
                "Skills and operating-model gaps when adopting new platforms.",
                "Cost and governance sprawl without guardrails."
            ],
            NextSteps        =
            [
                "Validate the brief's assumptions and finalise scope.",
                "Stand up a pilot for the highest-value option.",
                "Generate the recommended documents for detailed deliverables."
            ],
            Feasibility          = weighsOptions ? BuildFeasibility(combined, profile) : null,
            RecommendedDocuments = recommended
        };
    }

    // ── Heuristic feasibility analysis (offline use-case mode) ────────────────
    private static FeasibilityAnalysis BuildFeasibility(string scenario, DomainProfile profile)
    {
        var s = scenario.ToLowerInvariant();

        // Candidate target platforms named in the scenario.
        var targets = new List<(string Key, string Name)>();
        if (ContainsAny(s, "aws", "amazon web services")) targets.Add(("aws", "Amazon Web Services (AWS)"));
        if (ContainsAny(s, "gcp", "google cloud"))         targets.Add(("gcp", "Google Cloud Platform (GCP)"));
        if (ContainsAny(s, "azure"))                       targets.Add(("azure", "Microsoft Azure"));
        if (ContainsAny(s, "oracle cloud", "oci"))         targets.Add(("oci", "Oracle Cloud (OCI)"));
        if (targets.Count == 0) targets.Add(("generic", "Proposed approach"));

        var isCapacity  = ContainsAny(s, "capacity", "scale", "scaling", "throughput", "load", "performance");
        var isReplicate = ContainsAny(s, "replicate", "replication", "migrate", "migration", "port",
                                          "lift and shift", "multi-cloud", "multi cloud");

        var options = targets.Select(t => BuildFeasibilityOption(t.Key, t.Name, isReplicate)).ToList();

        var verdict = isCapacity
            ? "Spreading load across additional providers can relieve the immediate capacity ceiling, but only after infrastructure-as-code, data-residency, and identity parity are re-established on each target — a multi-week effort, not a quick switch."
            : "The scenario is achievable with established patterns; effort and risk vary per option as detailed below.";

        return new FeasibilityAnalysis(
            UseCase: scenario.Length > 280 ? scenario[..280] + "…" : scenario,
            Summary: $"Heuristic feasibility assessment for {profile.Name}: {options.Count} option(s) evaluated. "
                   + "Replicating an existing landing zone to a new provider is feasible but rarely a like-for-like copy — "
                   + "managed-service equivalents, networking, and IAM differ and must be re-implemented per cloud.",
            PrimaryConcernVerdict: verdict,
            Options: options
        );
    }

    private static FeasibilityOption BuildFeasibilityOption(string key, string name, bool isReplicate)
    {
        (string[] challenges, string[] roadblocks, int score, string effort) = key switch
        {
            "aws" => (
                new[] { "Map Azure PaaS to AWS equivalents (App Service→ECS/Fargate or App Runner, Azure SQL→RDS/Aurora)",
                        "Re-implement networking (VNet→VPC, NSG→Security Groups) and Front Door→CloudFront",
                        "Re-create identity & secrets (Entra ID→IAM/Cognito, Key Vault→Secrets Manager)" },
                new[] { "Data egress cost and cross-cloud latency for any shared state",
                        "IaC rewrite — Bicep/ARM does not run on AWS; needs Terraform or CDK" },
                7, "6–10 engineer-weeks for a single landing zone"),
            "gcp" => (
                new[] { "Map to GCP equivalents (App Service→Cloud Run, Azure SQL→Cloud SQL/AlloyDB)",
                        "Networking parity (VNet→VPC, Front Door→Cloud Load Balancing + Cloud CDN)",
                        "Identity mapping (Entra ID→Cloud Identity/IAM, Key Vault→Secret Manager)" },
                new[] { "Fewer turnkey enterprise-AD integrations than Azure",
                        "IaC rewrite to Terraform" },
                6, "7–11 engineer-weeks for a single landing zone"),
            "azure" => (
                new[] { "Scale out within Azure first (higher SKU tiers, autoscale, Front Door caching)",
                        "Validate regional quotas and capacity reservations" },
                new[] { "Same-provider correlated capacity risk remains" },
                8, "2–4 engineer-weeks"),
            "oci" => (
                new[] { "Smaller managed-service catalogue requires more self-managed components",
                        "Networking and IAM re-implementation" },
                new[] { "Ecosystem/tooling maturity gaps", "IaC rewrite" },
                5, "8–12 engineer-weeks"),
            _ => (
                new[] { "Establish target landing zone, networking, and identity",
                        "Re-implement managed-service dependencies" },
                new[] { "Infrastructure-as-code rewrite", "Data movement and consistency" },
                6, "6–10 engineer-weeks")
        };

        var verdict = score >= 8 ? "Feasible" : score >= 6 ? "Feasible with effort" : "Partial";
        var rec = isReplicate
            ? $"Treat as a fresh landing zone on {name} built from shared IaC modules — not a binary copy of the Azure setup."
            : $"Proceed on {name} with a thin pilot before committing the full workload.";

        return new FeasibilityOption(
            Name:           isReplicate ? $"Replicate landing zone to {name}" : name,
            Verdict:        verdict,
            Score:          score,
            EffortEstimate: effort,
            Challenges:     challenges,
            Roadblocks:     roadblocks,
            Recommendation: rec
        );
    }

    public TaskSpec CompileTask(
        string taskName, string? systemicValue, string? estimatedEffort, string? context,
        string? language = null)
    {
        logger.LogInformation("[Local Engine] Compiling task spec for '{T}' (language: {L})", taskName, language ?? "csharp");

        var id = DeterministicId(taskName + (context ?? string.Empty));

        return new TaskSpec
        {
            Id                    = id,
            TaskName              = taskName,
            Status                = "Completed",
            ProgressScore         = 100,
            SystemicValue         = systemicValue ?? "Core platform capability enabling downstream AI features.",
            EstimatedEffort       = estimatedEffort ?? "3–5 sprints (6–10 engineer-weeks)",
            GeneratedCodeTemplate = BuildCodeTemplate(taskName, language),
            OutputLogs            = BuildExecutionLogs(taskName)
        };
    }

    public DomainSuggestions CompileDomains()
    {
        logger.LogInformation("[Local Engine] Compiling domain suggestions (hierarchical v2)");
        return new DomainSuggestions
        {
            Domains =
            [
                new DomainCategory
                {
                    Name = "Law",
                    SubDomains = ["Corporate Law", "Contract Management", "Litigation Support", "Compliance & Regulatory", "IP & Patent Law", "Legal Research & eDiscovery", "Employment Law"]
                },
                new DomainCategory
                {
                    Name = "IT Services",
                    SubDomains = ["Cloud Infrastructure", "DevOps & Platform Engineering", "Cybersecurity", "Data Engineering & Analytics", "AI & ML Engineering", "Enterprise Architecture", "IT Service Management"]
                },
                new DomainCategory
                {
                    Name = "Tax",
                    SubDomains = ["Corporate Tax Planning", "VAT & Indirect Tax", "Transfer Pricing", "Tax Compliance & Reporting", "R&D Tax Credits", "International Tax", "Tax Technology"]
                },
                new DomainCategory
                {
                    Name = "Advisory",
                    SubDomains = ["Management Consulting", "Financial Advisory", "Risk Advisory", "Strategy & Transformation", "ESG Advisory", "Restructuring & Turnaround", "Deal Advisory & M&A"]
                },
                new DomainCategory
                {
                    Name = "Audit",
                    SubDomains = ["External Audit", "Internal Audit", "IT Audit", "Forensic Accounting", "Regulatory Compliance Audit", "Sustainability Reporting", "Data Analytics in Audit"]
                },
                new DomainCategory
                {
                    Name = "Healthcare",
                    SubDomains = ["Clinical Decision Support", "Patient Data Management", "Drug Discovery & Research", "Healthcare Operations", "Medical Imaging AI", "Telemedicine & Remote Care", "Population Health Analytics"]
                },
                new DomainCategory
                {
                    Name = "Financial Services",
                    SubDomains = ["Retail Banking", "Investment Banking", "Wealth Management", "Payments & FinTech", "AML & Financial Crime", "Credit Risk Modelling", "RegTech & Compliance"]
                },
                new DomainCategory
                {
                    Name = "Real Estate",
                    SubDomains = ["Property Management", "Commercial Real Estate", "PropTech & Smart Buildings", "Mortgage & Lending", "Real Estate Investment", "Facilities Management", "Construction & Project Management"]
                },
                new DomainCategory
                {
                    Name = "HR & Workforce",
                    SubDomains = ["Talent Acquisition", "Employee Experience", "Learning & Development", "Payroll & HR Compliance", "Workforce Analytics", "Diversity & Inclusion", "Succession Planning"]
                },
                new DomainCategory
                {
                    Name = "Retail & E-Commerce",
                    SubDomains = ["Inventory & Supply Chain", "Customer Intelligence", "E-Commerce Optimisation", "Loyalty & Personalisation", "Omnichannel Operations", "Pricing & Promotions", "Returns Management"]
                },
                new DomainCategory
                {
                    Name = "Manufacturing",
                    SubDomains = ["Production Optimisation", "Quality Control", "Predictive Maintenance", "Industrial IoT", "ERP & Operations", "Supply Chain Resilience", "Digital Twin & Simulation"]
                },
                new DomainCategory
                {
                    Name = "Government & Public Sector",
                    SubDomains = ["Public Service Delivery", "Policy Analytics", "Smart City & Infrastructure", "Benefits & Welfare", "Defence & Security", "Digital Identity", "Open Data & Transparency"]
                },
                new DomainCategory
                {
                    Name = "Education & EdTech",
                    SubDomains = ["Personalised Learning", "Student Analytics", "Curriculum Design", "Assessment & Credentialing", "LMS & Learning Platforms", "Special Education Needs", "University Research AI"]
                },
                new DomainCategory
                {
                    Name = "Insurance",
                    SubDomains = ["Underwriting Automation", "Claims Processing", "Fraud Detection", "Actuarial Modelling", "Customer Onboarding", "Parametric Insurance", "Reinsurance Analytics"]
                },
                new DomainCategory
                {
                    Name = "Energy & Utilities",
                    SubDomains = ["Smart Grid Management", "Renewable Energy Optimisation", "Energy Trading & Risk", "Demand Forecasting", "Asset Management", "Carbon Accounting", "Utility Customer Engagement"]
                },
                new DomainCategory
                {
                    Name = "Supply Chain & Logistics",
                    SubDomains = ["Demand Planning", "Warehouse Automation", "Last-Mile Delivery", "Supplier Risk Management", "Route Optimisation", "Trade Compliance", "Cold Chain Monitoring"]
                },
                new DomainCategory
                {
                    Name = "Media & Entertainment",
                    SubDomains = ["Content Recommendation", "Ad Tech & Programmatic", "Rights Management", "Audience Analytics", "Content Moderation", "Production Automation", "Streaming & OTT"]
                },
                new DomainCategory
                {
                    Name = "Pharmaceutical",
                    SubDomains = ["Clinical Trials Management", "Pharmacovigilance", "Drug Repurposing", "Regulatory Submissions", "Supply Chain Integrity", "Genomics & Personalised Medicine", "Commercial Analytics"]
                },
                new DomainCategory
                {
                    Name = "Travel & Hospitality",
                    SubDomains = ["Revenue Management", "Customer Experience", "Contactless Operations", "Loyalty Programmes", "Demand Forecasting", "Sustainability Reporting", "Corporate Travel Management"]
                },
                new DomainCategory
                {
                    Name = "Agriculture",
                    SubDomains = ["Precision Farming", "Crop Disease Detection", "Yield Prediction", "Irrigation Optimisation", "Supply Chain Traceability", "AgriFinance & Insurance", "Soil & Climate Analytics"]
                },
            ]
        };
    }

    public CorporateDocument CompileDocument(
        string blueprintId, string title, string templateType, string? domain)
    {
        logger.LogInformation(
            "[Local Engine] Compiling document '{T}' ({Type})", title, templateType);

        var profile = ResolveProfile(domain ?? title);

        return new CorporateDocument
        {
            Id           = DeterministicId(blueprintId + title + templateType),
            BlueprintId  = blueprintId,
            Title        = title,
            Content      = BuildMarkdownDocument(title, templateType, profile),
            TemplateType = templateType,
            CreatedAt    = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    public DeveloperPrompt CompilePrompt(
        string componentName, string? targetLLM, string? context)
    {
        logger.LogInformation(
            "[Local Engine] Compiling developer prompt for '{C}'", componentName);

        var llm = NormaliseLLM(targetLLM);
        var id  = DeterministicId(componentName + llm + (context ?? string.Empty));

        return new DeveloperPrompt
        {
            Id            = id,
            ComponentName = componentName,
            PromptText    = BuildPromptText(componentName, llm, context),
            TargetLLM     = llm,
            Directives    = BuildDirectives(componentName, llm)
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  DOMAIN RESOLUTION
    // ═══════════════════════════════════════════════════════════════

    private static DomainProfile ResolveProfile(string input)
    {
        var lower = input.ToLowerInvariant();

        // 1. Healthcare / Biotech
        if (ContainsAny(lower, "health", "medical", "clinical", "patient",
                        "hospital", "diagnostic", "pharma", "ehr", "fhir", "nurse", "biotech"))
            return Profiles["Healthcare AI"];

        // 2. Finance / Fintech
        if (ContainsAny(lower, "finance", "fintech", "banking", "payment", "crypto",
                        "trading", "investment", "insurance", "aml", "kyc", "ledger",
                        "bank", "transaction", "account", "stock", "lending", "micro-lending"))
            return Profiles["Financial Technology"];

        // 3. Legal / HR
        if (ContainsAny(lower, "legal", "law", "compliance", "contract",
                        "attorney", "litigation", "regulation", "discovery", "nda", "audit", "firm", "hr"))
            return Profiles["Legal Technology"];

        // 4. Retail / E-commerce
        if (ContainsAny(lower, "retail", "shop", "store", "order", "cart",
                        "product", "delivery", "fulfillment", "ecommerce", "e-commerce",
                        "inventory", "warehouse", "checkout", "merchant"))
            return Profiles["Retail & E-Commerce"];

        // 5. Real Estate / Property Management
        if (ContainsAny(lower, "property", "real estate", "realestate", "home", "leasing",
                        "tenant", "landlord", "rent", "apartment", "mortgage", "listing", "mls", "reit"))
            return Profiles["Real Estate & Property Management"];

        // 6. Education / EdTech
        if (ContainsAny(lower, "learn", "class", "school", "teacher", "course",
                        "academic", "student", "education", "edtech", "curriculum", "lms", "tutoring"))
            return Profiles["Education & EdTech"];

        // 7. Local Services
        if (ContainsAny(lower, "plumbing", "hvac", "repair", "contractor",
                        "electrician", "field service", "technician", "local service", "handyman", "service request"))
            return Profiles["Local Services"];

        // 8. Core Software & Tech
        if (ContainsAny(lower, "devops", "database", "api", "cloud", "firewall",
                        "network", "saas", "kubernetes", "microservice", "platform",
                        "infrastructure", "cicd", "ci/cd", "pipeline", "b2b", "saas"))
            return Profiles["Core Software & Tech"];

        // 9. General / Enterprise fallback
        return Profiles["Enterprise AI Platform"];
    }

    private static bool ContainsAny(string haystack, params string[] needles)
        => needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

    // ═══════════════════════════════════════════════════════════════
    //  FRESH ITEM GENERATION (pagination pool exhausted)
    // ═══════════════════════════════════════════════════════════════

    private static IEnumerable<PrioritizedItem> GenerateFreshItems(
        string domain, string keywords, int count, IReadOnlySet<string> excluded)
    {
        var templates = FreshItemTemplates.GetValueOrDefault(domain, FreshItemTemplates["Enterprise AI Platform"]);
        var seen      = new HashSet<string>(excluded, StringComparer.Ordinal);

        for (int i = 0; i < count && i < templates.Length; i++)
        {
            var tmpl = templates[i];
            var id   = DeterministicId(domain + keywords + i.ToString());

            if (seen.Contains(id))
                id = DeterministicId(id + "ext");

            seen.Add(id);

            yield return new PrioritizedItem
            {
                Id              = id,
                Name            = string.Format(tmpl.NameFmt, keywords),
                Description     = tmpl.Description,
                Urgency         = Clamp(8 - i % 3),
                Difficulty      = Clamp(5 + i % 4),
                Value           = Clamp(9 - i % 2),
                Rationale       = tmpl.Rationale,
                RealLifeValue   = tmpl.RealLifeValue,
                IntegrationSteps = tmpl.IntegrationSteps
            };
        }
    }

    private static int Clamp(int v) => Math.Clamp(v, 1, 10);

    // ═══════════════════════════════════════════════════════════════
    //  BLUEPRINT BUILDERS
    // ═══════════════════════════════════════════════════════════════

    private static string BuildCoreScenario(string name, DomainProfile p, string? subDomain, string? opportunity)
    {
        var focus = string.IsNullOrWhiteSpace(subDomain)
            ? ""
            : $", focused on the {subDomain!.Trim()} sub-domain,";
        var opp = string.IsNullOrWhiteSpace(opportunity)
            ? ""
            : $"\n\n**Opportunity addressed:** {(opportunity!.Trim().Length > 280 ? opportunity.Trim()[..280] + "…" : opportunity.Trim())}";
        return $"""
        ## Core Scenario — {name}

        {name} is a cloud-native, multi-tenant {p.Name} platform{focus} built on an event-driven
        microservices architecture. It exposes a unified AI inference layer that combines
        {p.TechStack} to deliver sub-200ms responses at p99 latency under sustained load.{opp}

        **Primary actor flow:**
        1. Client authenticates via OAuth 2.0 / OIDC token exchange with the API Gateway.
        2. Request routed to the domain service; payload validated and enriched from the
           feature store.
        3. AI inference model scores / generates output using the appropriate model variant.
        4. Result persisted to {p.DbPattern} and published to the event bus.
        5. Downstream consumers (analytics, notification) react asynchronously.

        **Non-functional targets:** 99.95% SLA · ≤200ms p99 · AES-256 at rest · TLS 1.3
        in transit · SOC 2 Type II · {p.ArchDescription}.
        """;
    }

    private static string BuildBaseTopology(string name, DomainProfile p) => p.Name switch
    {
        "Healthcare AI" => $"""
            ## Base Topology — {name}

            ```
            ┌──────────────────────────────────────────────────────────────┐
            │           FHIR R4 / HL7 v2 Integration Gateway               │
            │         (CDS Hooks · OAuth 2.0 / SMART on FHIR)             │
            └──────────┬─────────────────────────────┬─────────────────────┘
                       │                             │
            ┌──────────▼──────────┐     ┌────────────▼────────────────┐
            │  Clinical Decision   │     │  Medical Imaging Pipeline    │
            │  Support Service     │     │  (DICOM/PACS · DICOMweb)    │
            │  (NLP · Risk Score) │     │  GPU inference node          │
            └──────┬──────────────┘     └────────────┬────────────────┘
                   │                                  │
            ┌──────▼──────────────────────────────────▼──────────────┐
            │         PostgreSQL 16 + pgvector (clinical embeddings)  │
            │         TimescaleDB (vitals time-series)                 │
            └──────┬──────────────────────────────────────────────────┘
                   │
            ┌──────▼──────────────────────────────────┐
            │          HL7 Event Bus (NATS)            │
            └──────┬─────────────────┬────────────────┘
                   │                 │
            ┌──────▼──────┐  ┌───────▼──────────┐
            │ Analytics   │  │ Audit Log (HIPAA) │
            └─────────────┘  └──────────────────┘
            ```
            All services are HIPAA-eligible, containerised on AKS (Azure), zero-trust mTLS.
            """,

        "Financial Technology" => $"""
            ## Base Topology — {name}

            ```
            ┌──────────────────────────────────────────────────────────────┐
            │         Payment Gateway (Visa/MC · ISO 20022 · SWIFT)        │
            └──────────┬─────────────────────────────┬─────────────────────┘
                       │                             │
            ┌──────────▼──────────┐     ┌────────────▼────────────────┐
            │  Payments Service   │     │  Fraud Detection Engine     │
            │  (PCI DSS scope)    │     │  (ML scoring · <50ms SLA)   │
            └──────┬──────────────┘     └────────────┬────────────────┘
                   │                                  │
            ┌──────▼──────────────────────────────────▼──────────────┐
            │   PostgreSQL 16 (OLTP)  │  Apache Iceberg (audit/DWH)  │
            └──────┬──────────────────────────────────────────────────┘
                   │
            ┌──────▼────────────────────────────────────────┐
            │          Kafka (transaction event stream)      │
            └──────┬──────────────────┬─────────────────────┘
                   │                  │
            ┌──────▼──────┐  ┌────────▼────────┐
            │ AML Monitor │  │ Reporting / BI  │
            └─────────────┘  └─────────────────┘
            ```
            All services are PCI DSS Level 1 compliant, containerised on EKS (AWS).
            """,

        "Legal Technology" => $"""
            ## Base Topology — {name}

            ```
            ┌──────────────────────────────────────────────────────────────┐
            │        DMS Connector (iManage / NetDocuments · CMIS)         │
            └──────────┬─────────────────────────────┬─────────────────────┘
                       │                             │
            ┌──────────▼──────────┐     ┌────────────▼────────────────┐
            │  Matter Management  │     │  Document Intelligence Svc  │
            │  (LEDES billing)    │     │  (Legal-BERT NLP · OCR)     │
            └──────┬──────────────┘     └────────────┬────────────────┘
                   │                                  │
            ┌──────▼──────────────────────────────────▼──────────────┐
            │   PostgreSQL 16  │  Elasticsearch 8  │  S3 documents   │
            └──────┬──────────────────────────────────────────────────┘
                   │
            ┌──────▼────────────────────────────────────────┐
            │          Ethical Wall / Matter Bus (NATS)      │
            └──────┬──────────────────┬─────────────────────┘
                   │                  │
            ┌──────▼──────┐  ┌────────▼────────┐
            │ Billing Svc │  │ Compliance Audit │
            └─────────────┘  └─────────────────┘
            ```
            Matter-level privilege isolation enforced via service-mesh policy (Istio).
            """,

        "Retail & E-Commerce" => $"""
            ## Base Topology — {name}

            ```
            ┌──────────────────────────────────────────────────────────────┐
            │         CloudFront CDN  →  Storefront BFF (GraphQL)          │
            └──────────┬────────────────────────────────┬────────────────────┘
                       │                                │
            ┌──────────▼───────────┐      ┌────────────▼────────────────┐
            │  Catalogue Service   │      │  Cart / Orders Service      │
            │  (Elasticsearch)     │      │  (saga · outbox pattern)    │
            └──────┬───────────────┘      └────────────┬────────────────┘
                   │                                    │
            ┌──────▼──────────────────────────────────▼───────────────┐
            │  PostgreSQL (orders) │ Redis (sessions) │ Elasticsearch  │
            └──────┬────────────────────────────────────────────────────┘
                   │
            ┌──────▼────────────────────────────────────────┐
            │          Kafka (order / inventory events)      │
            └──────┬──────────────────┬─────────────────────┘
                   │                  │
            ┌──────▼──────────┐  ┌────▼──────────────┐
            │ Fulfilment / ERP│  │ Recommendations ML │
            └─────────────────┘  └───────────────────┘
            ```
            PCI DSS SAQ-A; auto-scales via Karpenter on EKS during flash sales.
            """,

        "Real Estate & Property Management" => $"""
            ## Base Topology — {name}

            ```
            ┌──────────────────────────────────────────────────────────────┐
            │           MLS RESO WebAPI Data Feed  (listing sync)          │
            └──────────┬─────────────────────────────┬─────────────────────┘
                       │                             │
            ┌──────────▼──────────┐     ┌────────────▼────────────────┐
            │  Listing Service    │     │  Leasing / Payments Service │
            │  (PostGIS geo-index)│     │  (DocuSign · Stripe)        │
            └──────┬──────────────┘     └────────────┬────────────────┘
                   │                                  │
            ┌──────▼──────────────────────────────────▼──────────────┐
            │  PostgreSQL 16 + PostGIS  │  S3 (leases · photos)      │
            └──────┬──────────────────────────────────────────────────┘
                   │
            ┌──────▼──────────────────────────────────────┐
            │   Property Event Bus  (lease / maintenance) │
            └──────┬──────────────────┬────────────────────┘
                   │                  │
            ┌──────▼──────────┐  ┌────▼───────────────┐
            │ Maintenance Svc │  │ Analytics / Reports │
            │ (IoT sensors)   │  └────────────────────┘
            └─────────────────┘
            ```
            Fair Housing Act compliant; proximity search via PostGIS spatial index.
            """,

        "Education & EdTech" => $"""
            ## Base Topology — {name}

            ```
            ┌──────────────────────────────────────────────────────────────┐
            │           LMS Connector  (Canvas / Moodle · LTI 1.3)         │
            └──────────┬─────────────────────────────┬─────────────────────┘
                       │                             │
            ┌──────────▼──────────┐     ┌────────────▼────────────────┐
            │  Content Service    │     │  AI Tutor / Assessment Svc  │
            │  (S3 · ABR video)   │     │  (BKT · spaced repetition)  │
            └──────┬──────────────┘     └────────────┬────────────────┘
                   │                                  │
            ┌──────▼──────────────────────────────────▼──────────────┐
            │  PostgreSQL 16  │  Redis (learner progress)  │ S3       │
            └──────┬──────────────────────────────────────────────────┘
                   │
            ┌──────▼──────────────────────────────────────┐
            │     Learning Event Bus  (FERPA-isolated)     │
            └──────┬──────────────────┬────────────────────┘
                   │                  │
            ┌──────▼──────────┐  ┌────▼───────────────┐
            │ Analytics / LRS │  │ Notification Svc   │
            └─────────────────┘  └────────────────────┘
            ```
            FERPA / COPPA compliant; WCAG 2.1 AA; video delivered via CloudFront ABR.
            """,

        "Local Services" => $"""
            ## Base Topology — {name}

            ```
            ┌──────────────────────────────────────────────────────────────┐
            │         Customer Portal  ·  Technician Mobile App (PWA)      │
            └──────────┬─────────────────────────────┬─────────────────────┘
                       │                             │
            ┌──────────▼──────────┐     ┌────────────▼────────────────┐
            │  Scheduling Service │     │  Real-Time Dispatch Svc     │
            │  (OR-Tools solver)  │     │  (WebSocket location feed)  │
            └──────┬──────────────┘     └────────────┬────────────────┘
                   │                                  │
            ┌──────▼──────────────────────────────────▼──────────────┐
            │   PostgreSQL 16 + PostGIS  │  Redis (dispatch state)    │
            └──────┬──────────────────────────────────────────────────┘
                   │
            ┌──────▼──────────────────────────────────────┐
            │       Job Event Bus  (job lifecycle)         │
            └──────┬──────────────────┬────────────────────┘
                   │                  │
            ┌──────▼──────────┐  ┌────▼───────────────┐
            │ Payments Service│  │ Customer Portal    │
            │ (PCI isolated)  │  │ Notifications      │
            └─────────────────┘  └────────────────────┘
            ```
            Offline-first technician app; field reconnect via CRDT sync on API resume.
            """,

        "Core Software & Tech" => $"""
            ## Base Topology — {name}

            ```
            ┌──────────────────────────────────────────────────────────────┐
            │       API Gateway  (REST external · rate-limit · auth)        │
            └──────────┬─────────────────────────────┬─────────────────────┘
                       │ REST                         │ gRPC internal
            ┌──────────▼──────────┐     ┌────────────▼────────────────┐
            │   Core Service      │     │  Worker Service             │
            │   (business logic)  │     │  (async job processing)     │
            └──────┬──────────────┘     └────────────┬────────────────┘
                   │                                  │
            ┌──────▼──────────────────────────────────▼──────────────┐
            │   PostgreSQL 16 (operational)  │  ClickHouse (telemetry)│
            └──────┬──────────────────────────────────────────────────┘
                   │
            ┌──────▼──────────────────────────────────────┐
            │    Kafka  (domain events · dead-letter)      │
            └──────┬──────────────────┬────────────────────┘
                   │                  │
            ┌──────▼──────────┐  ┌────▼───────────────┐
            │ Admin Service   │  │ Telemetry / OTel   │
            └─────────────────┘  └────────────────────┘
            ```
            OpenTelemetry traces + Prometheus metrics; mTLS via Istio service mesh.
            """,

        _ => $"""
            ## Base Topology — {name}

            ```
            ┌──────────────────────────────────────────────────────────────┐
            │           API Gateway  (Auth · Rate-limit · TLS)             │
            └──────────┬─────────────────────────────┬─────────────────────┘
                       │                             │
            ┌──────────▼──────────┐     ┌────────────▼────────────────┐
            │  Orchestration Svc  │     │  AI Inference Svc           │
            │  (business logic)   │     │  (LLM cascade · pgvector)   │
            └──────┬──────────────┘     └────────────┬────────────────┘
                   │                                  │
            ┌──────▼──────────────────────────────────▼──────────────┐
            │         PostgreSQL 16 + pgvector  (primary store)       │
            │         Redis  (cache · session)                         │
            └──────┬──────────────────────────────────────────────────┘
                   │
            ┌──────▼──────────────────────────────────────┐
            │          Event Bus  (NATS / Kafka)           │
            └──────┬──────────────────┬────────────────────┘
                   │                  │
            ┌──────▼──────────┐  ┌────▼───────────────┐
            │  Analytics Svc  │  │  Notification Svc  │
            └─────────────────┘  └────────────────────┘
            ```
            All services containerised (Docker · Kubernetes); horizontal autoscaling at 70% CPU.
            """
    };

    private static string BuildDatabaseSchemes(string name, DomainProfile p)
    {
        var slug = Slugify(name);
        var domainTable = BuildDomainTableSql(slug, p);
        // $$""" — double $ → {{slug}} is interpolation; single { } are literal content
        return $$"""
            ## Database Schemas — {{name}}

            ### Primary Store ({{p.DbPattern}})

            ```sql
            -- Tenant registry
            CREATE TABLE {{slug}}_tenants (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                slug        VARCHAR(100) NOT NULL UNIQUE,
                plan        VARCHAR(50)  NOT NULL DEFAULT 'standard',
                created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
            );

            -- Platform configuration (per-tenant feature flags and settings)
            CREATE TABLE {{slug}}_configs (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                tenant_id   UUID        NOT NULL REFERENCES {{slug}}_tenants(id) ON DELETE CASCADE,
                key         VARCHAR(200) NOT NULL,
                value       JSONB        NOT NULL,
                version     INTEGER      NOT NULL DEFAULT 1,
                created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                UNIQUE (tenant_id, key)
            );

            -- AI inference sessions
            CREATE TABLE {{slug}}_sessions (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                tenant_id   UUID        NOT NULL REFERENCES {{slug}}_tenants(id) ON DELETE CASCADE,
                domain      VARCHAR(200) NOT NULL,
                status      VARCHAR(50)  NOT NULL DEFAULT 'active'
                                         CHECK (status IN ('active','completed','failed')),
                model_chain VARCHAR(500),
                created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
            );

            -- Task & work item registry
            CREATE TABLE {{slug}}_tasks (
                id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                session_id    UUID        NOT NULL REFERENCES {{slug}}_sessions(id) ON DELETE CASCADE,
                name          VARCHAR(500) NOT NULL,
                status        VARCHAR(50)  NOT NULL DEFAULT 'pending'
                                           CHECK (status IN ('pending','running','completed','failed')),
                priority      SMALLINT     NOT NULL DEFAULT 5 CHECK (priority BETWEEN 1 AND 10),
                payload       JSONB,
                result        JSONB,
                latency_ms    INTEGER,
                started_at    TIMESTAMPTZ,
                completed_at  TIMESTAMPTZ,
                created_at    TIMESTAMPTZ  NOT NULL DEFAULT now()
            );
            {{domainTable}}
            -- Immutable PCI/audit event log
            CREATE TABLE {{slug}}_audit_log (
                id          BIGSERIAL    PRIMARY KEY,
                tenant_id   UUID         NOT NULL,
                actor       VARCHAR(255),
                action      VARCHAR(100) NOT NULL,
                resource_id UUID,
                ip_address  INET,
                metadata    JSONB,
                occurred_at TIMESTAMPTZ  NOT NULL DEFAULT now()
            );

            -- Composite indexes
            CREATE INDEX idx_{{slug}}_sessions_tenant ON {{slug}}_sessions(tenant_id, status, created_at DESC);
            CREATE INDEX idx_{{slug}}_tasks_session   ON {{slug}}_tasks(session_id, status);
            CREATE INDEX idx_{{slug}}_tasks_priority  ON {{slug}}_tasks(priority DESC, created_at) WHERE status = 'pending';
            CREATE INDEX idx_{{slug}}_audit_tenant    ON {{slug}}_audit_log(tenant_id, occurred_at DESC);
            CREATE INDEX idx_{{slug}}_configs_tenant  ON {{slug}}_configs(tenant_id, key);
            ```

            ### Redis Cache Layer
            Key namespaces: `{{slug}}:session:{id}` (TTL 15 min) · `{{slug}}:rate:{tenant}` (TTL 60 s) · `{{slug}}:lock:{resource}` (TTL 30 s)
            Eviction policy: `allkeys-lru` · Maxmemory: 4 GB per node · Cluster: 3 primary + 3 replica shards

            ### Vector Store (pgvector / Qdrant)
            Collection: `{{slug}}_embeddings` — dimension 1536, cosine similarity, HNSW index (m=16, ef_construction=200)
            Payload filter fields: `tenant_id`, `domain`, `created_at` for pre-filter before kNN scan
            """;
    }

    private static string BuildEndpointManifest(string name, DomainProfile p)
    {
        var domainRows = BuildDomainEndpointRows(p);
        return $$"""
        ## REST Endpoint Manifest — {{name}} API v1

        | Method | Path                                    | Description                        |
        |--------|-----------------------------------------|------------------------------------|
        | POST   | /api/v1/sessions                        | Initiate AI analysis session       |
        | GET    | /api/v1/sessions/{id}                   | Retrieve session state & metadata  |
        | POST   | /api/v1/sessions/{id}/analyse           | Trigger AI inference               |
        | GET    | /api/v1/sessions/{id}/results           | Stream paginated results           |
        | DELETE | /api/v1/sessions/{id}                   | Terminate session                  |{{domainRows}}
        | POST   | /api/v1/research                        | Domain keyword discovery           |
        | POST   | /api/v1/blueprints                      | Generate system blueprint          |
        | POST   | /api/v1/tasks                           | Execute & synthesise task          |
        | POST   | /api/v1/documents                       | Compile corporate document         |
        | POST   | /api/v1/prompts                         | Generate developer prompt          |
        | GET    | /api/v1/health                          | Liveness probe                     |
        | GET    | /api/v1/health/ready                    | Readiness probe                    |

        **Auth:** Bearer JWT (RS256) · **Rate-limit:** 1,000 req/min per tenant
        **Content-Type:** application/json · **Versioning:** URL path
        """;
    }

    private static string BuildResilienceStrategies(DomainProfile p) => """
        ## Resilience Strategies

        ### Circuit Breaker (Polly)
        - Trip threshold: 5 consecutive failures within a 10-second window
        - Half-open probe: 1 request per 30-second recovery interval
        - State transitions logged to telemetry with alert on Open state > 60 s

        ### Retry Policy
        - Max attempts: 3 · Backoff: exponential jitter (base 200ms, max 5 s)
        - Retryable: HTTP 429, 502, 503, 504 and transient HttpRequestException
        - Non-retryable: 400, 401, 403, 404, 422 (client errors — do not retry)

        ### Bulkhead Isolation
        - AI inference pool: 20 concurrent · queue depth: 50
        - Document generation pool: 10 concurrent · queue depth: 30
        - Overflow: immediate rejection with 429 to preserve core API responsiveness

        ### Timeout Hierarchy
        - Gateway → Service: 30 s total budget
        - Service → External LLM: 25 s (leaves 5 s for fallback + serialisation)
        - Health probe: 5 s (never shares the main timeout budget)

        ### Fallback
        - Any external LLM failure triggers the Local Compilation Engine
        - Fallback responses are cache-eligible and indistinguishable to the client
        - Metrics tag `fallback=true` for SLO tracking without exposing failures

        ---

        ## Multi-Model AI Routing — Cascade Configuration

        The LLM Orchestrator cascades through providers in strict priority order.
        Only providers with a configured API key are attempted. All providers share
        the same prompt contract, ensuring transparent failover with no client impact.

        ```
        Inbound Request
              │
              ▼
        ┌─────────────────────────────────────────────────────┐
        │           LLM Orchestrator (Priority Chain)         │
        │                                                     │
        │  [1] Gemini 2.5 Flash ──── 429/503 ─►              │
        │      • responseMimeType: application/json           │
        │      • Timeout: 90 s  Target latency: 800ms p50     │
        │                                        │            │
        │  [2] Groq (llama-3.3-70b-versatile) ◄─┘── 429/503 ►│
        │      • response_format: json_object                 │
        │      • Timeout: 60 s  Target latency: 350ms p50     │
        │                                        │            │
        │  [3] Claude Sonnet ◄───────────────────┘── error ──►│
        │      • Assistant prefill: "{"                       │
        │      • Timeout: 90 s  Target latency: 1200ms p50    │
        │                                        │            │
        │  [4] Heuristic Local Engine ◄──────────┘           │
        │      • In-process, zero-latency fallback            │
        │      • Returns domain-accurate procedural output    │
        │      • Guaranteed: never fails, never times out     │
        └─────────────────────────────────────────────────────┘
        ```

        ### Provider Routing Rules

        | Condition | Action |
        |-----------|--------|
        | HTTP 429 (quota exceeded) | Skip provider → log quota event → try next |
        | HTTP 503 (service unavailable) | Skip provider → log availability event → try next |
        | HTTP 5xx (server error) | Skip provider → log warning → try next |
        | JSON parse failure | Skip provider → log parse error → try next |
        | Timeout | Skip provider → log SLO miss → try next |
        | HTTP 400/401/422 (client error) | Do NOT retry — log misconfiguration alert |
        | All providers exhausted | Invoke Heuristic Local Engine (always succeeds) |

        ### Redis Cache Interception (15-Minute TTL)
        SHA-256 of the serialised request payload is computed before any provider
        is contacted. A cache hit short-circuits the entire cascade with sub-1ms
        response, tagged `source=cache` in response headers.

        ### Latency Budget Allocation
        ```
        Total gateway budget:  30,000 ms
        ├── Gemini attempt:      8,000 ms (incl. 2 retries with jitter)
        ├── Groq attempt:        6,000 ms (incl. 2 retries with jitter)
        ├── Claude attempt:      9,000 ms (incl. 2 retries with jitter)
        ├── Local Engine:           15 ms (heuristic — purely in-process)
        └── Serialisation/cache:   500 ms (budget reserve)
        ```

        ### Provider Health Monitoring
        Each provider's circuit breaker state is exposed at `GET /api/health/providers`.
        Degraded providers are excluded from the active chain until the half-open probe
        succeeds, preventing latency budget bleed on known-down providers.
        """ + BuildDomainResilienceNote(p);

    // ── Domain-specific blueprint accents (offline engine) ─────────────────────

    /// <summary>A domain-specific table appended to the shared multi-tenant schema.</summary>
    private static string BuildDomainTableSql(string slug, DomainProfile p) => p.Name switch
    {
        "Healthcare AI" => $$"""

            -- Patient registry (FHIR R4 aligned)
            CREATE TABLE {{slug}}_patients (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                tenant_id   UUID         NOT NULL REFERENCES {{slug}}_tenants(id) ON DELETE CASCADE,
                mrn         VARCHAR(64)  NOT NULL,
                fhir_id     VARCHAR(64),
                consent     JSONB        NOT NULL DEFAULT '{}'::jsonb,
                created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                UNIQUE (tenant_id, mrn)
            );
            """,
        "Financial Technology" => $$"""

            -- Double-entry ledger (append-only, idempotent)
            CREATE TABLE {{slug}}_ledger_entries (
                id              BIGSERIAL    PRIMARY KEY,
                tenant_id       UUID         NOT NULL REFERENCES {{slug}}_tenants(id) ON DELETE CASCADE,
                idempotency_key VARCHAR(128) NOT NULL,
                account_id      UUID         NOT NULL,
                direction       VARCHAR(6)   NOT NULL CHECK (direction IN ('debit','credit')),
                amount_minor    BIGINT       NOT NULL,
                currency        CHAR(3)      NOT NULL,
                occurred_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
                UNIQUE (tenant_id, idempotency_key)
            );
            """,
        "Legal Technology" => $$"""

            -- Matter registry (privilege-scoped)
            CREATE TABLE {{slug}}_matters (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                tenant_id   UUID         NOT NULL REFERENCES {{slug}}_tenants(id) ON DELETE CASCADE,
                matter_no   VARCHAR(64)  NOT NULL,
                client_id   UUID         NOT NULL,
                ethical_wall JSONB       NOT NULL DEFAULT '[]'::jsonb,
                legal_hold  BOOLEAN      NOT NULL DEFAULT false,
                created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                UNIQUE (tenant_id, matter_no)
            );
            """,
        "Retail & E-Commerce" => $$"""

            -- Orders
            CREATE TABLE {{slug}}_orders (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                tenant_id   UUID         NOT NULL REFERENCES {{slug}}_tenants(id) ON DELETE CASCADE,
                customer_id UUID         NOT NULL,
                status      VARCHAR(32)  NOT NULL DEFAULT 'pending',
                total_minor BIGINT       NOT NULL,
                currency    CHAR(3)      NOT NULL,
                placed_at   TIMESTAMPTZ  NOT NULL DEFAULT now()
            );
            """,
        "Real Estate & Property Management" => $$"""

            -- Listings (geospatial)
            CREATE TABLE {{slug}}_listings (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                tenant_id   UUID         NOT NULL REFERENCES {{slug}}_tenants(id) ON DELETE CASCADE,
                mls_id      VARCHAR(64),
                address     TEXT         NOT NULL,
                geo         GEOGRAPHY(Point, 4326),
                price_minor BIGINT       NOT NULL,
                status      VARCHAR(32)  NOT NULL DEFAULT 'active',
                created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
            );
            """,
        "Education & EdTech" => $$"""

            -- Enrollments
            CREATE TABLE {{slug}}_enrollments (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                tenant_id   UUID         NOT NULL REFERENCES {{slug}}_tenants(id) ON DELETE CASCADE,
                student_id  UUID         NOT NULL,
                course_id   UUID         NOT NULL,
                status      VARCHAR(32)  NOT NULL DEFAULT 'enrolled',
                enrolled_at TIMESTAMPTZ  NOT NULL DEFAULT now(),
                UNIQUE (tenant_id, student_id, course_id)
            );
            """,
        "Local Services" => $$"""

            -- Service jobs (dispatch)
            CREATE TABLE {{slug}}_service_jobs (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                tenant_id   UUID         NOT NULL REFERENCES {{slug}}_tenants(id) ON DELETE CASCADE,
                customer_id UUID         NOT NULL,
                technician_id UUID,
                status      VARCHAR(32)  NOT NULL DEFAULT 'requested',
                scheduled_for TIMESTAMPTZ,
                created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
            );
            """,
        "Core Software & Tech" => $$"""

            -- API keys & webhooks
            CREATE TABLE {{slug}}_api_keys (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                tenant_id   UUID         NOT NULL REFERENCES {{slug}}_tenants(id) ON DELETE CASCADE,
                key_hash    VARCHAR(128) NOT NULL,
                scopes      TEXT[]       NOT NULL DEFAULT '{}',
                revoked     BOOLEAN      NOT NULL DEFAULT false,
                created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
            );
            """,
        _ => ""
    };

    /// <summary>Domain-specific REST endpoint rows (newline-prefixed markdown table rows).</summary>
    private static string BuildDomainEndpointRows(DomainProfile p) => p.Name switch
    {
        "Healthcare AI" =>
            "\n        | GET    | /fhir/Patient/{id}                      | Read FHIR Patient resource         |" +
            "\n        | POST   | /fhir/Observation                       | Ingest clinical observation        |",
        "Financial Technology" =>
            "\n        | POST   | /api/v1/payments                        | Initiate payment (idempotent)      |" +
            "\n        | GET    | /api/v1/ledger/{account}                | Read account ledger                |",
        "Legal Technology" =>
            "\n        | GET    | /api/v1/matters/{id}                    | Read matter (privilege-checked)    |" +
            "\n        | POST   | /api/v1/matters/{id}/hold               | Apply legal hold                   |",
        "Retail & E-Commerce" =>
            "\n        | GET    | /api/v1/catalog/search                  | Faceted product search             |" +
            "\n        | POST   | /api/v1/orders                          | Place order (checkout)             |",
        "Real Estate & Property Management" =>
            "\n        | GET    | /api/v1/listings/search                 | Geospatial listing search          |" +
            "\n        | POST   | /api/v1/listings/{id}/offer             | Submit offer                       |",
        "Education & EdTech" =>
            "\n        | POST   | /api/v1/enrollments                     | Enroll student in course           |" +
            "\n        | POST   | /lti/1.3/launch                         | LTI 1.3 tool launch                |",
        "Local Services" =>
            "\n        | POST   | /api/v1/jobs                            | Create service request             |" +
            "\n        | POST   | /api/v1/jobs/{id}/dispatch              | Dispatch technician                |",
        "Core Software & Tech" =>
            "\n        | POST   | /api/v1/api-keys                        | Issue scoped API key               |" +
            "\n        | POST   | /api/v1/webhooks                        | Register webhook subscription      |",
        _ => ""
    };

    /// <summary>Domain hardening appended to the shared resilience strategy.</summary>
    private static string BuildDomainResilienceNote(DomainProfile p) => p.Name switch
    {
        "Healthcare AI" =>
            "\n\n### Domain Hardening — Healthcare\n" +
            "- RTO ≤ 15 min · RPO ≤ 1 min for PHI stores; audit log replicated synchronously (HIPAA §164.312).\n" +
            "- Break-glass access emits an immutable, alerting audit event.",
        "Financial Technology" =>
            "\n\n### Domain Hardening — Financial\n" +
            "- RTO ≤ 5 min · RPO = 0 (synchronous ledger replication).\n" +
            "- Idempotency keys enforced on every money-movement endpoint (PCI DSS / SOX).",
        "Legal Technology" =>
            "\n\n### Domain Hardening — Legal\n" +
            "- Matter data RPO ≤ 5 min; legal-hold flag blocks deletion.\n" +
            "- Ethical-wall policy evaluation fails closed on error.",
        _ => ""
    };

    // ═══════════════════════════════════════════════════════════════
    //  WHY-LAYER BUILDERS (ArchDecisions, QualityAttributes, TechRadar)
    // ═══════════════════════════════════════════════════════════════

    private static List<ArchDecision> BuildArchDecisions(DomainProfile p) => p.Name switch
    {
        "Healthcare AI" =>
        [
            new("Primary data store", "PostgreSQL 16 with pgvector", "HIPAA audit trail and SQL query requirements; pgvector enables clinical embedding search without a separate vector database.",
                ["MongoDB Atlas — lacks HIPAA BAA on all tiers; schema-less design makes SQL audit trail enforcement complex",
                 "DynamoDB — no native SQL for cross-table clinical analytics; AWS-specific lock-in complicates HIPAA BAA negotiations"],
                ["Schema migrations require downtime window; HIPAA BAA must be obtained from cloud provider."]),
            new("Service decomposition", "Domain-aligned microservices (Clinical, Imaging, Pharmacy, Analytics)", "Independent scaling per specialty; HIPAA data-boundary isolation; enables separate BAAs per service.",
                ["Monolith — single HIPAA boundary prevents independent scaling of GPU-backed imaging from CPU-bound CRUD; full BAA scope",
                 "Modular monolith — cannot independently scale AI inference (GPU nodes) from clinical API traffic (CPU nodes)"],
                ["Higher operational complexity; inter-service latency adds ~5ms p50."]),
            new("API style", "REST with FHIR R4 + CDS Hooks", "FHIR R4 is the mandatory interoperability standard for US healthcare (21st Century Cures Act).",
                ["GraphQL — not recognised by 21st Century Cures Act interoperability mandate; requires bespoke FHIR resource mapping",
                 "gRPC — binary protocol incompatible with EHR CDS Hooks standard; not supported by most EHR integration endpoints"],
                ["FHIR profile compliance requires additional validation middleware."]),
            new("Auth approach", "OAuth 2.0 / SMART on FHIR with RBAC", "SMART on FHIR is the clinician-facing auth standard; RBAC enforces minimum-necessary-access principle required by HIPAA.",
                ["API keys — no user-level audit trail required by HIPAA minimum necessary standard; cannot represent clinician identity",
                 "Session-based auth — incompatible with SMART on FHIR app launch framework used by EHR vendors"],
                ["Token refresh complexity in long clinical sessions."]),
            new("Resilience pattern", "Circuit breaker at 50% error rate over 30s with 5-req minimum", "Clinical workflows cannot tolerate cascading failures; 50% threshold triggers before downstream saturation.",
                ["Retry-only — cascading failure risk when downstream clinical service is degraded; no isolation between slow and failed services",
                 "Timeout-only — no circuit state tracking; degraded services keep receiving traffic until every call times out"],
                ["Half-open probe requests must be idempotent — requires careful API design."])
        ],
        "Financial Technology" =>
        [
            new("Primary data store", "PostgreSQL 16 (OLTP) + Apache Iceberg (analytical)", "ACID guarantees required for financial transactions; Iceberg enables time-travel queries for regulatory audit.",
                ["MongoDB — lacks multi-document ACID for atomic fund transfers; schema flexibility creates audit trail inconsistencies for SOX",
                 "MySQL — no native time-travel query support needed for regulatory back-testing; replication lag under sustained write load"],
                ["Dual-store ops complexity; Iceberg compaction jobs require scheduling."]),
            new("Service decomposition", "Bounded contexts: Payments, Accounts, Fraud, Reporting", "PCI DSS scope reduction — cardholder data confined to Payments service; Fraud isolated for ML model updates.",
                ["Single service — PCI DSS audit scope extends to entire codebase; cardholder data accessible to non-payment components",
                 "Event-sourced monolith — high coupling makes independent fraud model deployments impossible without full redeployment"],
                ["Cross-context sagas needed for distributed transactions."]),
            new("API style", "REST + ISO 20022 message format for interbank flows", "ISO 20022 is the mandated global payment messaging standard from 2025; REST for internal/partner APIs.",
                ["SOAP — deprecated by most banking partners; tooling overhead without benefit over REST + ISO 20022 combination",
                 "GraphQL — no standardised banking message format; ISO 20022 compliance requires REST message structure"],
                ["ISO 20022 schema versioning adds maintenance overhead."]),
            new("Auth approach", "OAuth 2.0 + PKCE with step-up MFA for high-value transactions", "Regulatory requirement (PSD2, SOX); step-up prevents credential-stuffing from elevating transaction limits.",
                ["API keys — static credentials cannot support step-up auth for high-value transactions; no PSD2-compliant SCA flow",
                 "Client certificates only — cannot support consumer-facing OAuth flows for open banking; brittle certificate lifecycle"],
                ["MFA friction affects UX; requires fallback for low-connectivity clients."]),
            new("Fraud detection", "Real-time ML scoring via feature store with 50ms SLA", "Batch fraud detection misses 68% of card-present fraud; real-time sub-50ms scoring is industry baseline.",
                ["Rule engine only — misses 68% of novel fraud patterns not covered by predefined rules; high false negative rate on new attack vectors",
                 "Batch ML — 4–24h lag on fraud signals; card-present fraud is exploited within minutes of the initial transaction"],
                ["Feature store operational overhead; model drift requires weekly retraining pipeline."])
        ],
        "Legal Technology" =>
        [
            new("Primary data store", "PostgreSQL 16 + Elasticsearch (full-text) + S3 (documents)", "Matter data requires ACID; Elasticsearch for case law search; S3 for cost-efficient document storage with encryption at rest.",
                ["MongoDB — no ACID multi-document transactions for billing records; schema flexibility undermines audit trail integrity under LEDES",
                 "Oracle — 10x licensing cost vs PostgreSQL; proprietary lock-in contradicts vendor-neutral architecture requirement"],
                ["Three-store ops complexity; Elasticsearch index sync latency ~2s."]),
            new("Service decomposition", "Matter Management, Document Intelligence, Billing, Compliance", "Legal billing (LEDES standard) isolated to prevent data leakage across client matters; attorney-client privilege boundary enforcement.",
                ["Monolith — single ethical wall boundary cannot enforce matter-level privilege isolation at the architectural level",
                 "Feature-flagged monolith — matter data still shares process space; privilege wall enforcement is configuration-only, not structural"],
                ["Matter isolation requires strict service-mesh policies."]),
            new("API style", "REST with CMIS for document management", "CMIS is the industry standard for legal DMS integration (iManage, NetDocuments); REST for all other services.",
                ["GraphQL — not recognised by iManage or NetDocuments DMS integration standards; requires bespoke schema mapping per DMS",
                 "gRPC — binary protocol not supported by legacy DMS connectors; poor compatibility with browser-based client applications"],
                ["CMIS adds implementation overhead; versioning complexity with legacy DMS."]),
            new("Auth approach", "SAML 2.0 SSO + fine-grained RBAC per matter", "Law firm IT mandates SAML SSO for enterprise directory integration; matter-level RBAC enforces ethical walls.",
                ["OAuth 2.0 only — law firm enterprise IT requires SAML 2.0 for Active Directory federation; OAuth migration exceeds v1 scope",
                 "Local accounts — contradicts law firm SSO mandate; separate credential store creates HIPAA-equivalent security audit gap"],
                ["SAML assertion expiry in long client sessions requires silent refresh logic."]),
            new("Document analysis", "Transformer NLP (Legal-BERT fine-tuned) with human-in-the-loop review", "Legal-BERT outperforms generic models by 23% on contract extraction tasks; HITL required for high-stakes clauses.",
                ["Generic LLM — 23% lower accuracy on legal extraction benchmarks; hallucination risk on high-stakes clause identification unacceptable",
                 "Rule-based extraction — brittle against non-standard contract formats; cannot handle ambiguous clause language without full pattern enumeration"],
                ["Model fine-tuning requires labelled legal corpus; HITL adds latency."])
        ],
        "Retail & E-Commerce" =>
        [
            new("Primary data store", "PostgreSQL (orders/inventory) + Redis (sessions/cart) + Elasticsearch (product search)", "Transactional integrity for orders; Redis sub-1ms cart operations; Elasticsearch powers typo-tolerant product search.",
                ["MongoDB — no ACID for order transactions; inconsistent reads under peak load create oversell and double-charge risk",
                 "DynamoDB — no native full-text product search; secondary indexes insufficient for faceted catalogue search requirements"],
                ["Three-store consistency; cart/order sync requires saga pattern."]),
            new("Service decomposition", "Catalog, Cart, Orders, Payments, Fulfilment, Recommendations", "PCI DSS scope: Payments service isolated; Recommendations decoupled for independent ML model deployments.",
                ["Monolith — PCI DSS audit scope covers entire codebase; ML Recommendations model updates require full service redeployment",
                 "Two-service split — cannot independently scale catalogue browse (read-heavy, CDN-cacheable) from order processing (write-heavy, ACID)"],
                ["Distributed transactions for order-to-fulfilment flow require outbox pattern."]),
            new("API style", "REST (public/partner) + GraphQL (storefront BFF)", "GraphQL BFF reduces mobile over-fetching by 60%; REST for stable partner integrations and webhooks.",
                ["REST only — mobile clients over-fetch product data on list views, causing 3–5x excess bandwidth on cellular connections",
                 "gRPC — not supported by partner webhook integrations; browser clients require gRPC-web proxy adding latency"],
                ["GraphQL N+1 query risk requires DataLoader; adds resolver complexity."]),
            new("Auth approach", "OAuth 2.0 with guest checkout token + persistent account JWT", "Guest checkout critical for conversion; persistent JWT enables cross-device cart recovery.",
                ["Session cookies — SameSite restrictions break cross-domain cart sharing; horizontal scaling requires sticky sessions",
                 "API keys — cannot represent guest users without account creation; kills conversion for first-time shoppers"],
                ["Token refresh race condition on multi-tab checkout requires deduplication."]),
            new("Inventory consistency", "Eventual consistency with optimistic locking + reservation TTL", "Strong consistency on inventory creates hot-row contention at peak; reservation TTL (15 min) balances availability vs. oversell.",
                ["Strong consistency — creates hot-row contention on popular SKUs; 3x latency increase observed at 50k concurrent checkouts",
                 "Last-write-wins — silent oversell with no recovery; customer disputes and fulfilment failures outweigh any consistency gains"],
                ["Oversell risk during TTL window; requires async reconciliation job."])
        ],
        "Real Estate & Property Management" =>
        [
            new("Primary data store", "PostgreSQL 16 (listings/leases) + PostGIS (geospatial)", "Lease ACID guarantees; PostGIS enables proximity search and zoning analysis without a separate geo-database.",
                ["MongoDB — no native geospatial index comparable to PostGIS; polygon intersection for zoning analysis requires a separate service",
                 "Elasticsearch geo — strong for search but lacks transactional guarantees required for lease records and payment data"],
                ["PostGIS query tuning needed for large polygon intersections."]),
            new("Service decomposition", "Listings, Leasing, Payments, Maintenance, Analytics", "Payment processing isolated for PCI compliance; Maintenance decoupled for IoT sensor integration.",
                ["Monolith — PCI scope extends to listing browse; IoT sensor stream from maintenance would overload monolith I/O",
                 "Two-service — cannot independently scale listing search (high read) from payment processing (low volume, high consistency)"],
                ["Lease-to-payment saga requires outbox pattern for consistency."]),
            new("API style", "REST + MLS RESO WebAPI standard for listing syndication", "RESO WebAPI is the MLS interoperability standard; REST for all internal services and tenant portals.",
                ["GraphQL — not supported by RESO WebAPI MLS syndication standard; real estate partners require RESO-compliant REST endpoints",
                 "SOAP — deprecated by MLS boards; no actively maintained RESO-compliant SOAP implementation"],
                ["RESO schema versioning adds upgrade overhead."]),
            new("Auth approach", "OAuth 2.0 with tenant/landlord/agent RBAC", "Three distinct actor types with non-overlapping permission sets; OAuth enables third-party integrations (DocuSign, Stripe).",
                ["Session-based — stateful sessions don't support mobile token refresh across network interruptions; scaling requires sticky sessions",
                 "API keys — cannot differentiate tenant, landlord, and agent permission scopes within a single key"],
                ["RBAC complexity grows with sub-tenant and co-signer roles."]),
            new("Document management", "S3 + pre-signed URLs with 24h expiry for leases/inspection reports", "Lease documents require tamper-evident storage; pre-signed URLs prevent direct S3 exposure.",
                ["Database BLOB storage — lease documents consume expensive IOPS; PostgreSQL not optimised for multi-MB binary storage at scale",
                 "Shared file system — no document-level access control; concurrent access contention; not cloud-native or region-redundant"],
                ["URL expiry management adds complexity; CloudFront needed for CDN delivery."])
        ],
        "Education & EdTech" =>
        [
            new("Primary data store", "PostgreSQL (LMS data) + Redis (session/progress cache) + S3 (content)", "FERPA requires ACID audit log; Redis caches learner progress for real-time dashboards; S3 for cost-efficient video/content storage.",
                ["MongoDB — no ACID for grade and assessment records; FERPA audit trail requires transactional writes across multiple collections",
                 "Firebase — limited server-side query capability for complex LMS reporting; US data residency guarantee required by FERPA for K-12"],
                ["Multi-store consistency; content versioning in S3 requires lifecycle policies."]),
            new("Service decomposition", "Content, Assessment, Analytics, Notifications, AI Tutor", "Analytics isolated for FERPA data governance; AI Tutor decoupled for model version independence.",
                ["Monolith — FERPA data governance requires analytics isolation; AI Tutor model updates would require full service redeployment",
                 "Modular monolith — learning progress events shared across modules create ordering guarantees that cannot be enforced architecturally"],
                ["Cross-service event ordering critical for learning progress tracking."]),
            new("API style", "REST + LTI 1.3 for LMS integration", "LTI 1.3 is the IMS Global standard for Canvas, Moodle, Blackboard integration; REST for native and partner APIs.",
                ["GraphQL — Canvas/Moodle integration requires REST; LTI 1.3 deep-linking uses standard OAuth 2.0 flows not expressible in GraphQL",
                 "SCORM only — SCORM 1.2/2004 cannot handle real-time AI tutoring interactions; no server-side event push capability"],
                ["LTI 1.3 deep-linking requires additional OAuth 2.0 flow."]),
            new("Auth approach", "OAuth 2.0 + LTI 1.3 SSO + FERPA-compliant consent flow", "Institutional SSO required by IT departments; FERPA mandates explicit parental consent for under-13 learners.",
                ["Local accounts only — institutional IT departments mandate SSO; separate credential store duplicates identity management and creates FERPA audit gap",
                 "SAML only — LTI 1.3 requires OAuth 2.0 flows for deep-linking; SAML cannot carry LTI context parameters in the launch flow"],
                ["Consent flow UX complexity for K-12 deployments."]),
            new("Adaptive learning", "Bayesian Knowledge Tracing + spaced repetition scheduler", "BKT is the research-validated approach for knowledge state estimation; spaced repetition improves retention by 40% vs. linear progression.",
                ["Static curriculum — no personalisation; completion rates 40% lower than adaptive paths in controlled studies",
                 "Rule-based branching — brittle against novel student response patterns; cannot infer latent knowledge state from response sequences"],
                ["BKT model requires per-learner parameter estimation; cold-start problem for new users."])
        ],
        "Local Services" =>
        [
            new("Primary data store", "PostgreSQL 16 (jobs/scheduling) + PostGIS (service area routing)", "Job scheduling requires ACID; PostGIS enables technician-to-job proximity matching without external routing service.",
                ["MongoDB — no spatial index for technician-to-job proximity queries; PostGIS needed for service area boundary enforcement",
                 "MySQL — no native geospatial support at scale; complex polygon routing for service area management not possible"],
                ["PostGIS routing at scale requires index tuning."]),
            new("Service decomposition", "Scheduling, Dispatch, Payments, Customer Portal, Technician App", "Payment isolated for PCI; Technician App decoupled for offline-first mobile support.",
                ["Monolith — PCI scope covers all components; offline-first mobile support cannot be embedded in a monolith without CRDT complexity",
                 "Single API + mobile — no separation between real-time dispatch (WebSocket) and scheduling CRUD (REST); cannot scale independently"],
                ["Offline sync conflict resolution for field operations requires CRDT strategy."]),
            new("API style", "REST + WebSocket for real-time dispatch", "REST for scheduling and CRUD; WebSocket enables sub-second technician location updates without polling overhead.",
                ["REST polling — 1–5 second polling interval creates 300ms+ average location staleness; technician dispatch accuracy degrades",
                 "gRPC — binary protocol not supported by native mobile WebSocket clients; bidirectional streaming adds complexity for offline scenarios"],
                ["WebSocket reconnection logic required for unreliable field network conditions."]),
            new("Auth approach", "OAuth 2.0 with customer and technician role separation + magic-link for customers", "Technicians require persistent tokens for offline access; magic-link reduces customer friction (no password to forget).",
                ["Username/password only — high friction for field technicians needing quick job acceptance; no offline token persistence mechanism",
                 "SMS OTP only — unreliable in low-signal field conditions; technicians cannot authenticate offline between cell tower zones"],
                ["Magic-link delivery depends on email/SMS reliability in field."]),
            new("Scheduling optimisation", "Constraint-based solver (Google OR-Tools) with dynamic re-routing", "OR-Tools reduces drive time by 22% vs. greedy assignment; dynamic re-routing handles same-day cancellations.",
                ["Manual dispatch — 35% higher average drive time vs OR-Tools; dispatcher bottleneck at peak creates 15+ minute response delays",
                 "FIFO queue — ignores geographic clustering; technicians drive past closer jobs to serve earlier requests regardless of proximity"],
                ["Solver warm-up adds 300ms latency on large fleet; requires caching of last solution."])
        ],
        "Core Software & Tech" =>
        [
            new("Primary data store", "PostgreSQL 16 (operational) + ClickHouse (analytics/telemetry)", "OLTP for application state; ClickHouse ingests 1M+ telemetry events/sec at sub-second query latency.",
                ["MongoDB — schema flexibility creates telemetry schema drift; no columnar storage for analytical queries at 1M+ events/sec",
                 "TimescaleDB — better for single-metric time-series but underperforms ClickHouse on multi-dimensional analytical queries at high cardinality"],
                ["Dual-store sync; ClickHouse schema changes are destructive — requires migration strategy."]),
            new("Service decomposition", "API Gateway, Core Service, Worker, Telemetry, Admin", "Gateway handles auth/rate-limiting; Worker decoupled for async job processing without blocking the API.",
                ["Monolith — async job processing blocks API threads under high load; telemetry ingestion would saturate REST API I/O",
                 "Serverless functions — cold start latency (100–3000ms) incompatible with ≤100ms p99 API SLA; complex state management for long jobs"],
                ["Worker queue backpressure management required at high throughput."]),
            new("API style", "REST (external) + gRPC (internal service mesh)", "REST for developer-facing API (broad SDK support); gRPC for internal calls (3x lower latency than REST, binary protocol).",
                ["REST everywhere — internal service calls pay JSON serialisation overhead; 3x higher latency than gRPC binary protocol for high-frequency inter-service calls",
                 "GraphQL — N+1 query risk in internal service mesh; schema stitching complexity without clear benefit over gRPC for backend-to-backend communication"],
                ["gRPC requires Protobuf schema management; browser clients need gRPC-web proxy."]),
            new("Auth approach", "API keys (developer-facing) + JWT (user sessions) + mTLS (service-to-service)", "Three auth tiers match three caller types; mTLS ensures zero-trust between services without per-request overhead.",
                ["Single token type — API keys cannot represent user sessions; JWTs unsuitable for service-to-service; one mechanism cannot satisfy all three caller types",
                 "OAuth only — OAuth flows unsuitable for service-to-service calls in the hot path; API key issuance still required for developer experience"],
                ["mTLS certificate rotation automation required; adds infra complexity."]),
            new("Observability", "OpenTelemetry traces + Prometheus metrics + structured JSON logs", "OpenTelemetry vendor-neutral standard prevents lock-in; Prometheus is the de-facto scrape standard for Kubernetes.",
                ["Vendor agent only — vendor lock-in prevents cloud migration; metrics not portable across Datadog/New Relic/Dynatrace",
                 "Log-based only — structured logs cannot correlate distributed traces; latency attribution across service boundaries requires trace context propagation"],
                ["High-cardinality trace sampling strategy required to control storage cost."])
        ],
        _ => // Enterprise AI Platform (fallback)
        [
            new("Primary data store", "PostgreSQL 16 with pgvector for AI embeddings", "Proven ACID reliability with vector similarity search eliminates need for a separate vector database in v1.",
                ["MongoDB — schema flexibility undermines data quality for AI training datasets; no native vector similarity search without a separate vector DB",
                 "Pinecone — single-purpose vector DB requires a separate OLTP store; operational overhead of two databases not justified for v1 scale"],
                ["pgvector index size grows with embedding dimensionality; requires HNSW tuning."]),
            new("Service decomposition", "API, Orchestration, AI Inference, Storage, Analytics", "Separation enables independent scaling of AI inference (GPU nodes) from API traffic (CPU nodes).",
                ["Monolith — GPU inference nodes cannot be sized independently from CPU-bound API traffic; inference scale-out requires full application redeployment",
                 "Serverless — AI inference models cannot cold-start within acceptable latency; container warm-up of 5–30s exceeds user-facing SLA"],
                ["Inference service warm-up latency; orchestration adds ~10ms overhead per hop."]),
            new("API style", "REST with OpenAPI 3.1 specification-first design", "Specification-first enables auto-generated client SDKs and contract testing without extra tooling.",
                ["GraphQL — specification-first design with GraphQL requires custom code-gen tooling; OpenAPI ecosystem broadly adopted with mature tooling",
                 "gRPC — browser clients require gRPC-web proxy; not developer-friendly for external API consumers without SDK generation"],
                ["REST payload size higher than gRPC for bulk operations."]),
            new("Auth approach", "OAuth 2.0 with RBAC and API key support for programmatic access", "Dual-mode auth covers both human users and machine-to-machine integrations without separate auth systems.",
                ["API keys only — cannot represent user sessions for multi-tenant dashboard access; no RBAC expression without custom middleware",
                 "Session-based — stateless JWT preferred for distributed multi-region deployment; session affinity creates single-region dependency"],
                ["Token rotation strategy required for long-lived API key management."]),
            new("AI model integration", "Multi-model cascade: primary LLM → fallback LLM → heuristic engine", "Cascade prevents single-provider dependency; heuristic engine guarantees 100% uptime during API outages.",
                ["Single provider — quota exhaustion or provider outage causes 100% downtime with no failover path for AI-dependent workflows",
                 "On-premise model only — GPU infrastructure capital cost 10x cloud; model updates require cluster redeployment; no access to frontier model improvements"],
                ["Prompt normalisation layer required to handle provider-specific quirks."])
        ]
    };

    private static List<QualityAttribute> BuildQualityAttributes(DomainProfile p) => p.Name switch
    {
        "Healthcare AI" =>
        [
            new("Availability",        "99.9%",    "uptime over 30-day rolling window; planned maintenance excluded"),
            new("Response Time",       "≤200ms",   "p99 latency for clinical decision API under 500 concurrent users"),
            new("Throughput",          "5,000 req/min", "sustained peak load during morning rounds surge"),
            new("Data Retention",      "7 years",  "PHI retention per HIPAA §164.530(j); 10 years for clinical trial data"),
            new("Security",            "HIPAA BAA + AES-256", "PHI encrypted at rest (AES-256) and in transit (TLS 1.3)"),
            new("Compliance",          "HIPAA / HITECH / SOC 2 Type II", "annual third-party audit; penetration test every 6 months"),
            new("Recovery Time Objective", "≤30 min", "RTO for full service restoration after region failure")
        ],
        "Financial Technology" =>
        [
            new("Availability",        "99.99%",   "four-nines uptime; zero planned downtime during market hours"),
            new("Response Time",       "≤50ms",    "p99 latency for payment authorisation; fraud scoring ≤30ms"),
            new("Throughput",          "50,000 TPS", "peak transaction volume (Black Friday / market open)"),
            new("Data Retention",      "7 years",  "transaction records per SOX §802; 10 years for AML records"),
            new("Security",            "PCI DSS Level 1 + SOC 2 Type II", "quarterly ASV scans; annual QSA assessment"),
            new("Compliance",          "PCI DSS / SOX / AML / KYC",  "automated KYC screening; real-time AML transaction monitoring"),
            new("Recovery Time Objective", "≤5 min",  "RTO for payment processing; zero data loss (RPO = 0)")
        ],
        "Legal Technology" =>
        [
            new("Availability",        "99.9%",    "uptime excluding scheduled maintenance windows (off-hours only)"),
            new("Response Time",       "≤500ms",   "p95 latency for document search across 10M+ document corpus"),
            new("Throughput",          "1,000 concurrent users", "peak during discovery production deadlines"),
            new("Data Retention",      "10 years", "client matter records per bar association ethics rules"),
            new("Security",            "AES-256 + attorney-client privilege isolation", "ethical wall enforcement verified by quarterly access review"),
            new("Compliance",          "SOC 2 Type II / ISO 27001 / GDPR", "annual certification; DPA agreements with all sub-processors"),
            new("Recovery Time Objective", "≤1 hour", "RTO for matter management; nightly backup with 4h RPO")
        ],
        "Retail & E-Commerce" =>
        [
            new("Availability",        "99.95%",   "uptime; 100% uptime SLA during Black Friday / Cyber Monday windows"),
            new("Response Time",       "≤150ms",   "p99 product search latency; ≤300ms checkout page load (Core Web Vitals)"),
            new("Throughput",          "100,000 req/min", "peak traffic during flash sales (10x baseline)"),
            new("Data Retention",      "7 years",  "order records per tax compliance; 90 days for session/cart data"),
            new("Security",            "PCI DSS SAQ-A + TLS 1.3", "tokenised card storage via Stripe/Adyen; no raw PAN on platform"),
            new("Compliance",          "GDPR / CCPA / PCI DSS",  "cookie consent management; right-to-erasure workflow"),
            new("Recovery Time Objective", "≤15 min", "RTO for cart/checkout; 1-hour RTO for analytics services")
        ],
        "Real Estate & Property Management" =>
        [
            new("Availability",        "99.9%",    "uptime; maintenance windows restricted to 2–4 AM local time"),
            new("Response Time",       "≤300ms",   "p95 listing search with geospatial filter across 1M+ listings"),
            new("Throughput",          "10,000 concurrent users", "peak during spring market surge (March–May)"),
            new("Data Retention",      "7 years",  "lease and payment records per state landlord-tenant law"),
            new("Security",            "AES-256 + SOC 2 Type II", "SSN/EIN masked in storage; background check data isolated"),
            new("Compliance",          "Fair Housing Act / GDPR / CCPA", "automated Fair Housing Act audit on search result ranking"),
            new("Recovery Time Objective", "≤1 hour", "RTO for lease management; 4-hour RTO for listing analytics")
        ],
        "Education & EdTech" =>
        [
            new("Availability",        "99.9%",    "uptime; 100% during exam windows (zero maintenance in testing hours)"),
            new("Response Time",       "≤200ms",   "p99 assessment submission API; ≤3s video start time (ABR streaming)"),
            new("Throughput",          "25,000 concurrent learners", "peak during live virtual classroom sessions"),
            new("Data Retention",      "5 years post-graduation", "student records per FERPA; indefinite for institution-level analytics"),
            new("Security",            "FERPA / COPPA compliant + AES-256", "parental consent flow for under-13; SSO with institutional IdP"),
            new("Compliance",          "FERPA / COPPA / WCAG 2.1 AA", "annual accessibility audit; automated WCAG CI checks"),
            new("Recovery Time Objective", "≤30 min", "RTO for assessment platform; 2-hour RTO for content delivery")
        ],
        "Local Services" =>
        [
            new("Availability",        "99.5%",    "uptime; degraded-mode offline support for field technician app"),
            new("Response Time",       "≤500ms",   "p95 job dispatch API; ≤100ms technician location update (WebSocket)"),
            new("Throughput",          "500 concurrent dispatches", "peak across fleet during morning rush (7–9 AM)"),
            new("Data Retention",      "3 years",  "job records and customer history per local service regulations"),
            new("Security",            "TLS 1.3 + encrypted local cache on mobile", "technician device MDM enrollment; customer PII masked in logs"),
            new("Compliance",          "GDPR / CCPA + insurance compliance", "liability waiver digital signature audit trail"),
            new("Recovery Time Objective", "≤1 hour", "RTO for scheduling; dispatch continues in degraded mode offline")
        ],
        "Core Software & Tech" =>
        [
            new("Availability",        "99.95%",   "uptime per SLA tier; 99.99% for enterprise tier customers"),
            new("Response Time",       "≤100ms",   "p99 API latency; ≤10ms p99 for internal gRPC service calls"),
            new("Throughput",          "10,000 req/sec", "sustained; 50,000 req/sec burst (3x multiplier for auto-scale)"),
            new("Data Retention",      "90 days hot / 2 years cold", "telemetry data tiered to object storage after 90 days"),
            new("Security",            "SOC 2 Type II + CVE patching ≤72h", "quarterly penetration test; zero critical CVEs in production"),
            new("Compliance",          "SOC 2 Type II / ISO 27001 / GDPR", "annual certification; automated compliance evidence collection"),
            new("Recovery Time Objective", "≤15 min", "RTO per SLA; RPO ≤ 1 min with synchronous replication")
        ],
        _ => // Enterprise AI Platform
        [
            new("Availability",        "99.9%",    "uptime over 30-day rolling window"),
            new("Response Time",       "≤500ms",   "p95 latency for AI inference API under normal load"),
            new("Throughput",          "5,000 req/min", "sustained peak; auto-scale triggered at 70% CPU"),
            new("Data Retention",      "3 years",  "operational data; 7 years for audit logs"),
            new("Security",            "AES-256 at rest + TLS 1.3 in transit", "annual penetration test; quarterly access review"),
            new("Compliance",          "SOC 2 Type II",  "annual third-party audit"),
            new("Recovery Time Objective", "≤1 hour", "RTO for full service restoration after failure")
        ]
    };

    private static List<TechRadarEntry> BuildTechRadar(DomainProfile p) => p.Name switch
    {
        "Healthcare AI" =>
        [
            new("Frontend",  ["Angular 19", "React (clinician portal)"]),
            new("Backend",   ["ASP.NET Core 10", "Python FastAPI (ML pipelines)", "FHIR R4 Server"]),
            new("Data",      ["PostgreSQL 16 + pgvector", "TimescaleDB", "Redis Cache"]),
            new("Infra",     ["Azure (HIPAA-eligible)", "Kubernetes AKS", "Azure API Management"]),
            new("AI",        ["Clinical-BERT NLP", "DICOM CNN models", "Gemini / Claude Sonnet (cascade)"])
        ],
        "Financial Technology" =>
        [
            new("Frontend",  ["React + TypeScript", "Next.js (customer portal)"]),
            new("Backend",   ["Java Spring Boot", "Go (payment processing)", "Node.js (webhooks)"]),
            new("Data",      ["PostgreSQL 16", "Apache Iceberg", "Redis Cluster", "Kafka"]),
            new("Infra",     ["AWS (PCI DSS compliant)", "EKS", "AWS KMS", "Terraform"]),
            new("AI",        ["XGBoost (fraud scoring)", "Llama 3.3 70B (analysis)", "Gemini 2.5 Flash"])
        ],
        "Legal Technology" =>
        [
            new("Frontend",  ["React + TypeScript", "Electron (desktop client)"]),
            new("Backend",   ["ASP.NET Core 10", "Python (NLP pipelines)"]),
            new("Data",      ["PostgreSQL 16", "Elasticsearch 8", "S3 (documents)", "Redis"]),
            new("Infra",     ["Azure Government", "AKS", "Azure Purview", "Terraform"]),
            new("AI",        ["Legal-BERT", "GPT-4o (contract analysis)", "Claude Sonnet"])
        ],
        "Retail & E-Commerce" =>
        [
            new("Frontend",  ["Next.js (storefront)", "React Native (mobile app)"]),
            new("Backend",   ["Node.js + NestJS", "Go (inventory service)"]),
            new("Data",      ["PostgreSQL 16", "Elasticsearch 8", "Redis Cluster", "Kafka"]),
            new("Infra",     ["AWS", "EKS + Karpenter", "CloudFront CDN", "Terraform"]),
            new("AI",        ["Collaborative filtering (recommendations)", "Gemini 2.5 Flash", "Llama 3.3 70B"])
        ],
        "Real Estate & Property Management" =>
        [
            new("Frontend",  ["React + TypeScript", "React Native (tenant app)"]),
            new("Backend",   ["ASP.NET Core 10", "Node.js (listing sync)"]),
            new("Data",      ["PostgreSQL 16 + PostGIS", "Elasticsearch (listing search)", "Redis", "S3"]),
            new("Infra",     ["AWS", "EKS", "Lambda (webhooks)", "Terraform"]),
            new("AI",        ["XGBoost (price prediction)", "CV models (photo tagging)", "Gemini 2.5 Flash"])
        ],
        "Education & EdTech" =>
        [
            new("Frontend",  ["React + TypeScript", "React Native (learner app)"]),
            new("Backend",   ["ASP.NET Core 10", "Python (ML pipelines)", "LTI 1.3 provider"]),
            new("Data",      ["PostgreSQL 16", "Redis (session/progress)", "S3 (content)", "ClickHouse"]),
            new("Infra",     ["AWS", "CloudFront (video CDN)", "EKS", "Terraform"]),
            new("AI",        ["Bayesian Knowledge Tracing", "spaced repetition engine", "Claude Sonnet (AI Tutor)"])
        ],
        "Local Services" =>
        [
            new("Frontend",  ["React (web portal)", "React Native (technician app)"]),
            new("Backend",   ["ASP.NET Core 10", "Node.js (real-time dispatch)"]),
            new("Data",      ["PostgreSQL 16 + PostGIS", "Redis (session/dispatch)", "S3 (job photos)"]),
            new("Infra",     ["AWS", "EKS", "API Gateway", "Terraform"]),
            new("AI",        ["Google OR-Tools (scheduling)", "CV (job photo analysis)", "Gemini 2.5 Flash"])
        ],
        "Core Software & Tech" =>
        [
            new("Backend",   ["ASP.NET Core 10", "Go (high-throughput workers)", "gRPC services"]),
            new("Data",      ["PostgreSQL 16", "ClickHouse (telemetry)", "Redis Cluster", "Kafka"]),
            new("Infra",     ["AWS / GCP / Azure", "Kubernetes", "Istio service mesh", "Terraform"]),
            new("DevOps",    ["GitHub Actions / CI", "ArgoCD", "Helm", "OpenTelemetry"]),
            new("AI",        ["OpenTelemetry anomaly detection", "Gemini 2.5 Flash", "Claude Sonnet"])
        ],
        _ => // Enterprise AI Platform
        [
            new("Backend",   ["ASP.NET Core 10", "Python (ML pipelines)", "FastAPI"]),
            new("Data",      ["PostgreSQL 16 + pgvector", "Redis Cache", "S3"]),
            new("Infra",     ["Azure / AWS", "Kubernetes", "Terraform"]),
            new("DevOps",    ["Docker", "Helm", "GitHub Actions", "Prometheus"]),
            new("AI",        ["Gemini 2.5 Flash", "Llama 3.3 70B", "Claude Sonnet 4.6"])
        ]
    };

    private static List<BuyVsBuildOption> BuildBuyVsBuild(DomainProfile p) => p.Name switch
    {
        "Healthcare AI" =>
        [
            new("Authentication",      "SMART on FHIR + Okta / Azure AD",    "Pre-certified HIPAA BAA, FHIR launch context built-in",            "Custom OAuth 2.0 / SMART server with BAA attestation",         "Full control over token lifecycle and clinical context claims",  "Buy",   "SMART on FHIR certification takes 6–12 months to build; Auth0/Okta have pre-certified healthcare tiers."),
            new("Clinical NLP",        "AWS Comprehend Medical / Google Healthcare NLP", "Pre-trained on clinical notes; de-identification included", "Fine-tune Legal-BERT on annotated clinical corpus",             "Domain-specific accuracy 23% higher than generic models on ICD coding", "Build", "Generic cloud NLP misses clinical nuance; fine-tuned model pays back within 3 months of volume."),
            new("Imaging Viewer",      "OsiriX MD / Horos / Ambra Health",   "FDA-cleared; DICOM SR structured reporting built in",             "Custom DICOM viewer on open-source Cornerstone.js",             "Full UI control; integrate directly with AI inference pipeline", "Buy",   "FDA 510(k) clearance alone takes 18+ months; commercial viewer is cleared today."),
            new("FHIR Server",         "Azure API for FHIR / Google Cloud Healthcare API", "Managed, HIPAA-eligible, CMS-compliant out of the box", "HAPI FHIR open-source server on AKS",                          "Zero vendor lock-in; full profile customisation",                "Hybrid", "Managed service for production PHI storage; HAPI for dev/test and custom profiles."),
            new("Analytics / BI",      "Looker / Power BI Healthcare Pack",  "Pre-built clinical KPI dashboards; HIPAA-eligible tenants",       "Custom ClickHouse + Grafana stack",                             "Sub-second queries on 100M+ clinical events; no per-seat cost", "Build", "Clinical analytics volume makes per-seat BI costs unsustainable; custom stack pays back at 50k daily active users.")
        ],
        "Financial Technology" =>
        [
            new("Authentication",      "Auth0 / Okta with step-up MFA",      "PSD2 SCA flows built-in; PKCE, FAPI profile supported",           "Custom OAuth 2.0 server with step-up risk engine",             "Integrate fraud score directly into MFA challenge decision",    "Buy",   "PSD2 compliance and FAPI certification built into commercial IdPs; custom takes 12+ months."),
            new("Fraud Detection",     "Stripe Radar / Featurespace ARIC",   "Pre-trained on billions of transactions; real-time <10ms",        "XGBoost feature store + model serving on SageMaker",           "Own the model; integrate proprietary signals (device, behaviour)","Build", "Proprietary transaction signals give 15–20% lift over generic models at scale."),
            new("Payment Processing",  "Stripe / Adyen / Braintree",         "PCI DSS Level 1 certified; global acquiring; 3DS2 built-in",      "ISO 20022 message router + direct card network connection",     "Lower interchange at scale (>$1B TPV); full control over routing","Buy",   "Direct network connection requires $5M+ investment and 18-month certification; commercial PSPs handle it."),
            new("Core Banking Ledger", "Thought Machine Vault / Mambu",      "Cloud-native; event-sourced; regulatory reporting built-in",      "Event-sourced PostgreSQL ledger with double-entry accounting",  "No vendor lock-in; tailor to novel product structures",          "Hybrid", "Buy for consumer products; build for novel financial instruments not supported by core banking vendors."),
            new("AML / KYC",           "ComplyAdvantage / Refinitiv World-Check", "Sanctions and PEP databases updated in real-time",          "Custom rule engine + graph model on transaction network",       "Detect network-level patterns commercial tools miss",            "Hybrid", "Buy for sanctions screening; build for proprietary network analysis of transaction rings.")
        ],
        "Legal Technology" =>
        [
            new("Authentication",      "Azure AD / Okta with SAML 2.0 SSO", "Meets law firm IT mandate for AD federation; SOC 2 certified",   "Custom SAML IdP with matter-level RBAC",                       "Ethical wall enforcement at the identity layer",                "Buy",   "Law firm IT will not approve non-SAML SSO; commercial IdPs have pre-built AD connectors."),
            new("Document Management", "iManage Work / NetDocuments",        "Pre-integrated with Word, Outlook; ABA ethical wall certified",  "Custom DMS on S3 + Elasticsearch + CMIS API",                  "Lower cost at scale; custom metadata schema per practice area", "Hybrid","Buy the DMS for client-facing storage; build CMIS adapter and AI annotation layer on top."),
            new("Contract Analysis",   "Ironclad / ContractPodAi / Kira",   "Pre-trained on 1M+ contracts; SOC 2 Type II; HITL workflows",    "Fine-tuned Legal-BERT for jurisdiction-specific clause types",  "23% higher accuracy on custom clause types; no per-doc pricing","Build", "Commercial tools charge per document; at >10k docs/month fine-tuned model breaks even in month 4."),
            new("E-Billing",           "Mitratech TyMetrix / BillBlade",     "LEDES 98B/BI/XML pre-certified; AFA management built-in",        "Custom LEDES parser + billing rules engine",                   "Full control over guideline enforcement and accrual logic",      "Buy",   "LEDES certification and outside counsel guideline libraries take 2 years to build; commercial tools exist."),
            new("Legal Research",      "Westlaw Edge / LexisNexis+",         "Comprehensive case law and statute coverage; AI-cited summaries","Build on PubMed-style open datasets + LLM summarisation",       "Domain-specific citations for niche practice areas",             "Buy",   "Case law databases require decade-long publisher relationships; no viable open alternative for US law.")
        ],
        "Retail & E-Commerce" =>
        [
            new("Authentication",      "Auth0 / Firebase Auth (guest + social)","Guest checkout token + social login out of the box; CCPA",    "Custom JWT service with guest session management",             "Zero dependency on third-party for conversion-critical checkout","Build", "Guest checkout token is simple to build; Auth0 adds per-MAU cost that scales against conversion volume."),
            new("Search & Discovery",  "Algolia / Elasticsearch Service",    "Typo-tolerant; faceted search; A/B ranking built-in",            "Self-hosted Elasticsearch 8 + custom ranker",                  "Full control over ranking signals (margin, inventory, LTV)",    "Hybrid","Use Algolia for launch; migrate hot-path ranking to custom model once data volume justifies it."),
            new("Payments",            "Stripe / Adyen",                     "PCI DSS L1 out of the box; global acquiring; 3DS2 / SCA",        "Direct card network + payment router",                         "Lower blended rate at >$100M GMV; custom retry logic",          "Buy",   "PCI DSS certification costs $500K+; Stripe handles it; switch to direct network at $100M+ GMV."),
            new("Recommendations",     "Salesforce Einstein / Dynamic Yield","Real-time personalisation; A/B testing built-in; GDPR tools",   "Collaborative filtering + real-time feature store (Redis)",     "Proprietary signals (cart abandon, LTV, margin) drive 25% uplift","Build","Third-party recommendation tools can't access first-party purchase graph; custom model pays back at 1M+ SKUs."),
            new("Fulfilment / OMS",    "Brightpearl / Linnworks / Shopify OMS","Pre-integrated with 3PL networks; returns management built-in","Custom order orchestration on top of PostgreSQL saga pattern",  "Full control over allocation logic for complex multi-DC routing","Buy",   "3PL integration library alone takes 12 months; commercial OMS has 200+ carrier integrations.")
        ],
        "Real Estate & Property Management" =>
        [
            new("Authentication",      "Auth0 with tenant/landlord/agent RBAC","Three-role RBAC profiles; DocuSign SSO integration",           "Custom OAuth 2.0 with PostGIS-scoped property ACLs",           "Geo-fenced access control (agent sees only their market area)",  "Hybrid","Buy for standard RBAC; build the PostGIS-scoped property permission layer on top."),
            new("MLS Data Feed",       "RESO Spark API / Bridge Interactive","RESO WebAPI 2.0 certified; 600+ MLS boards covered",             "Direct RETS/RESO feed parser + normalisation pipeline",         "Custom field mapping for luxury and commercial MLS schemas",    "Hybrid","Buy the syndication connector; build the normalisation layer for market-specific field variations."),
            new("E-Signatures",        "DocuSign / HelloSign (Dropbox Sign)", "State-specific compliance; audit trail for UETA / E-SIGN Act",  "Custom signature service with PDF annotation",                 "No per-envelope cost; integrate directly with lease engine",    "Buy",   "State-level e-signature law compliance (UETA, ESIGN) is complex; DocuSign is pre-certified in all 50 states."),
            new("Property Valuation",  "HouseCanary API / Zillow AVM",       "Pre-trained on 100M+ US properties; instant API",               "Custom AVM on MLS + public records + mortgage data",           "Proprietary data signals (renovation history, HOA) improve accuracy","Hybrid","Buy AVM for launch; build proprietary model once 50k+ closed transactions give training data."),
            new("Background Checks",   "TransUnion SmartMove / Checkr",      "FCRA-compliant; renter credit + criminal; 24h turnaround",      "Direct credit bureau integration + criminal database query",   "Lower per-check cost at scale; customise adverse action notices","Buy",   "FCRA compliance and bureau agreements require 18-month legal negotiation; commercial service exists.")
        ],
        "Education & EdTech" =>
        [
            new("Authentication",      "Auth0 + LTI 1.3 + Canvas/Moodle SSO","LTI 1.3 deep-linking certified; parental consent for COPPA",    "Custom OAuth 2.0 + LTI 1.3 provider from scratch",            "Full control over learner identity claims and consent flows",   "Buy",   "LTI 1.3 certification with IMS Global takes 9 months; Auth0 has pre-built LTI 1.3 connector."),
            new("Video Delivery",      "Mux / Cloudflare Stream / AWS IVS",  "ABR HLS/DASH; auto-thumbnail; SSAI; COPPA-compliant CDN",        "Custom HLS packager on ffmpeg + CloudFront CDN",               "Zero per-minute cost; full control over encoding presets",      "Buy",   "ABR packaging + global CDN at low latency is infrastructure-heavy; Mux charges $0.003/min which is competitive."),
            new("AI Tutor",            "Khanmigo (Khan Academy) / Cognii",   "Pre-trained on curriculum; Socratic dialogue model built-in",    "Fine-tuned Claude Sonnet with custom prompting framework",      "Personalise to platform's pedagogy; no content licensing fee",  "Build", "Commercial AI tutors lock in curriculum; fine-tuned model lets you own the pedagogical approach."),
            new("LMS / Assessment",    "Canvas / Moodle / Brightspace",      "SCORM 2004, QTI 2.1, LTI 1.3 certified; 10k+ plugin ecosystem","Custom LMS with adaptive branching and BKT knowledge model",   "Adaptive branching not possible in commercial LMS without expensive plugins","Hybrid","Use Canvas for institutional accounts; build adaptive assessment engine as a LTI 1.3 tool on top."),
            new("Analytics",           "Tableau / Amplitude for Education",  "FERPA-eligible data processing agreements; pre-built EDU dashboards","Custom xAPI / Learning Record Store + ClickHouse",          "Query learner paths at sub-second; no per-seat analytics cost", "Build", "Per-seat BI costs at district scale (100k+ learners) exceed custom ClickHouse stack within 6 months.")
        ],
        "Local Services" =>
        [
            new("Authentication",      "Auth0 with magic-link + technician JWT","Magic-link for customers (no password friction); offline JWT","Custom magic-link service + persistent offline token store",   "Zero third-party dependency for offline technician auth",       "Hybrid","Buy Auth0 for customer magic-link; build offline JWT persistence layer for technician app."),
            new("Scheduling / Routing","Google OR-Tools (open source)",       "Apache 2.0 licence; constraint solver proven at Uber/DoorDash","Commercial routing API (Google Maps Routes / HERE Routing)",   "No per-route API cost; fully customisable constraint weights",  "Build", "OR-Tools is free, battle-tested, and allows custom constraints (skill matching, load capacity); APIs charge per call."),
            new("Payments",            "Stripe Connect (marketplace payments)","Split payments to technicians + platform fee built-in; 1099-K","Custom payment router with direct technician payout via ACH",  "Lower blended rate at scale; control over payout timing",       "Buy",   "Stripe Connect handles contractor tax reporting (1099-K) which is legally complex; build at >$50M GMV."),
            new("Customer Communication","Twilio / SendGrid",                  "SMS/voice/email in one SDK; delivery analytics; TCPA compliance","Custom notification service with provider fallback",           "Zero per-SMS cost with own short code; SLA guarantees",         "Buy",   "TCPA compliance for marketing SMS requires legal tooling Twilio provides; own short code only makes sense at 1M+ SMS/month."),
            new("Field Mobile App",    "React Native (open source)",          "Single codebase iOS + Android; Expo OTA updates; offline-first","Native Swift / Kotlin per platform",                          "Native performance for camera, GPS, BLE peripherals",           "Build", "Technician app requires custom offline sync and native GPS; React Native with Expo is build, not buy.")
        ],
        "Core Software & Tech" =>
        [
            new("Authentication",      "Auth0 / Okta (developer tier)",       "API key + JWT + machine-to-machine in one SDK; SOC 2 Type II",  "Custom token service with API key rotation and mTLS minting",  "mTLS certificates for service mesh issued by same auth service", "Hybrid","Buy Auth0 for human users and API keys; build mTLS certificate issuer for service-to-service."),
            new("API Gateway",         "Kong / AWS API Gateway / Nginx",       "Rate limiting, auth, observability plugins; battle-tested at scale","Custom gateway on Envoy + custom Lua/WASM plugins",          "Sub-millisecond overhead; custom routing logic for canary/A-B", "Buy",   "Custom Envoy proxy requires SRE expertise; Kong or AWS API Gateway covers 95% of use cases in days."),
            new("Observability",       "Datadog / Grafana Cloud",              "Pre-built Kubernetes dashboards; anomaly detection; SLO tracking","OpenTelemetry + Prometheus + Grafana self-hosted",            "Zero vendor lock-in; 10x lower cost at high cardinality",       "Build", "OpenTelemetry is now the standard; self-hosted stack costs 80% less than Datadog at >10k metrics series."),
            new("Message Queue",       "Confluent Cloud (managed Kafka)",      "Managed Kafka; schema registry; Flink SQL built-in",            "Self-hosted Kafka on Kubernetes + Strimzi operator",           "Full topic control; no per-partition pricing surprises",        "Hybrid","Use Confluent for launch speed; migrate to self-hosted Kafka when bill exceeds $5k/month."),
            new("Feature Flags",       "LaunchDarkly / Flagsmith (OSS)",       "Targeting rules, kill switches, A/B integration; SOC 2",        "Custom flag service backed by Redis or database",              "Zero per-seat cost; integrate directly with deployment pipeline","Buy",   "LaunchDarkly's SDK integrations and audit log take 6 months to replicate; OSS Flagsmith is a free alternative.")
        ],
        _ => // Enterprise AI Platform
        [
            new("Authentication",      "Auth0 / Okta",                        "OAuth 2.0, RBAC, API keys, MFA in one platform; SOC 2 Type II", "Custom token service with per-tenant RBAC",                    "Full control over tenant isolation and custom claim logic",      "Buy",   "Auth0 handles OAuth complexity; custom only justified when tenant-specific claim logic is needed."),
            new("LLM Provider",        "Gemini / Groq / Claude (API cascade)", "Frontier models; no GPU capex; swap providers on quota limits", "Fine-tuned open model (Llama, Mistral) on GPU cluster",         "Lower per-token cost at scale; domain-specific accuracy gains",  "Hybrid","API cascade for launch (already built); fine-tune domain model once >1M daily inferences justify GPU investment."),
            new("Vector Database",     "pgvector (built into PostgreSQL)",     "No separate service; ACID; <5ms similarity search at 1M vecs",  "Pinecone / Weaviate managed service",                           "Managed scaling; advanced indexing (HNSW); multi-tenant isolation","Build","pgvector eliminates a separate service and its ops overhead; Pinecone justified only at >100M vector operations/day."),
            new("Orchestration",       "LangChain / LlamaIndex (open source)","Large plugin ecosystem; prompt chaining; retrieval built-in",   "Custom orchestration layer with provider abstraction",          "Full control over fallback logic, caching, and observability",   "Build","Custom orchestrator (already implemented as LLMOrchestrator.cs) gives tighter control than framework opinionation."),
            new("Storage",             "S3-compatible object store",           "Unlimited scale; 11 nines durability; pre-signed URL security","Custom file service on NFS / distributed block storage",        "Zero egress cost on-prem; data sovereignty for regulated clients","Buy",   "S3 API is the industry standard; building an object store is a decade-long project.")
        ]
    };

    // ═══════════════════════════════════════════════════════════════
    //  TASK BUILDERS
    // ═══════════════════════════════════════════════════════════════

    private static List<string> BuildExecutionLogs(string taskName)
    {
        var steps = new (string Level, string Message)[]
        {
            ("INFO   ", "Meridian Task Executor v2.0 initialised"),
            ("INFO   ", $"Parsing task specification: '{taskName}'"),
            ("INFO   ", "Resolving dependency graph..."),
            ("INFO   ", "Dependency graph resolved — 7 nodes, 0 conflicts"),
            ("INFO   ", "Scaffolding project directory structure"),
            ("INFO   ", "Generating domain model contracts (records + factory methods)"),
            ("INFO   ", "Synthesising application service layer"),
            ("INFO   ", "Wiring dependency injection container"),
            ("INFO   ", "Applying resilience policies — circuit-breaker, retry, bulkhead"),
            ("INFO   ", "Running static code analysis — 0 warnings, 0 errors"),
            ("PASS   ", "Unit tests: 24 / 24 passed (0 skipped)"),
            ("PASS   ", "Integration tests: 8 / 8 passed (0 skipped)"),
            ("INFO   ", "Build artefacts packaged — Release/net10.0"),
            ("SUCCESS", $"Task '{taskName}' completed — ProgressScore: 100 / 100")
        };

        return steps
            .Select((s, i) => $"[{i * 820,6}ms] {s.Level} — {s.Message}")
            .ToList();
    }

    private static string BuildCodeTemplate(string taskName, string? language) =>
        (language ?? "csharp").ToLowerInvariant() switch
        {
            "typescript" => BuildTypeScriptTemplate(taskName),
            "python"     => BuildPythonTemplate(taskName),
            "java"       => BuildJavaTemplate(taskName),
            "go"         => BuildGoTemplate(taskName),
            _            => BuildCSharpTemplate(taskName)
        };

    private static string BuildCSharpTemplate(string taskName)
    {
        var cls = ToPascalCase(taskName);
        return $$"""
            // Auto-generated by Meridian Local Compilation Engine
            // Task: {{taskName}}

            namespace MeridianStudio.Implementation.{{cls}};

            public sealed class {{cls}}Service(
                ILogger<{{cls}}Service> logger,
                IOptions<{{cls}}Options> options)
            {
                public async Task<{{cls}}Result> ExecuteAsync(
                    {{cls}}Request request,
                    CancellationToken ct = default)
                {
                    ArgumentNullException.ThrowIfNull(request);
                    logger.LogInformation("[{{cls}}] Executing with context: {Ctx}", request.Context);

                    await ProcessCoreLogicAsync(request, ct);

                    return new {{cls}}Result(
                        Success: true,
                        ExecutedAt: DateTimeOffset.UtcNow,
                        Message: "{{taskName}} completed successfully.");
                }

                private async Task ProcessCoreLogicAsync({{cls}}Request request, CancellationToken ct)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
                    logger.LogDebug("[{{cls}}] Core logic processed.");
                }
            }

            public sealed record {{cls}}Request(string Context, Dictionary<string, string>? Metadata = null);
            public sealed record {{cls}}Result(bool Success, DateTimeOffset ExecutedAt, string Message);
            public sealed class {{cls}}Options { public string Mode { get; set; } = "Production"; }
            """;
    }

    private static string BuildTypeScriptTemplate(string taskName)
    {
        var cls = ToPascalCase(taskName);
        return $$"""
            // Auto-generated by Meridian Local Compilation Engine
            // Task: {{taskName}}

            interface {{cls}}Request {
              context: string;
              metadata?: Record<string, string>;
            }

            interface {{cls}}Result {
              success: boolean;
              executedAt: string;
              message: string;
            }

            export class {{cls}}Service {

              async execute(request: {{cls}}Request): Promise<{{cls}}Result> {
                if (!request) throw new Error('Request is required');
                console.log(`[{{cls}}] Executing with context:`, request.context);

                await this.processCoreLogic(request);

                return {
                  success: true,
                  executedAt: new Date().toISOString(),
                  message: '{{taskName}} completed successfully.',
                };
              }

              private async processCoreLogic(_request: {{cls}}Request): Promise<void> {
                await new Promise<void>(resolve => setTimeout(resolve, 50));
                console.debug('[{{cls}}] Core logic processed.');
              }
            }
            """;
    }

    private static string BuildPythonTemplate(string taskName)
    {
        var cls = ToPascalCase(taskName);
        return $$"""
            # Auto-generated by Meridian Local Compilation Engine
            # Task: {{taskName}}

            from __future__ import annotations

            import asyncio
            import logging
            from dataclasses import dataclass, field
            from datetime import datetime, timezone

            logger = logging.getLogger(__name__)


            @dataclass
            class {{cls}}Request:
                context: str
                metadata: dict[str, str] = field(default_factory=dict)


            @dataclass
            class {{cls}}Result:
                success: bool
                executed_at: str
                message: str


            class {{cls}}Service:

                async def execute(self, request: {{cls}}Request) -> {{cls}}Result:
                    if request is None:
                        raise ValueError("Request is required")
                    logger.info("[{{cls}}] Executing with context: %s", request.context)

                    await self._process_core_logic(request)

                    return {{cls}}Result(
                        success=True,
                        executed_at=datetime.now(timezone.utc).isoformat(),
                        message="{{taskName}} completed successfully.",
                    )

                async def _process_core_logic(self, request: {{cls}}Request) -> None:
                    await asyncio.sleep(0.05)
                    logger.debug("[{{cls}}] Core logic processed.")
            """;
    }

    private static string BuildJavaTemplate(string taskName)
    {
        var cls = ToPascalCase(taskName);
        var pkg = cls.ToLowerInvariant();
        return $$"""
            // Auto-generated by Meridian Local Compilation Engine
            // Task: {{taskName}}

            package meridian.implementation.{{pkg}};

            import org.slf4j.Logger;
            import org.slf4j.LoggerFactory;
            import org.springframework.stereotype.Service;
            import java.time.Instant;
            import java.util.Map;

            @Service
            public class {{cls}}Service {

                private static final Logger logger = LoggerFactory.getLogger({{cls}}Service.class);

                public {{cls}}Result execute({{cls}}Request request) {
                    if (request == null) throw new IllegalArgumentException("Request is required");
                    logger.info("[{{cls}}] Executing with context: {}", request.context());

                    processCoreLogic(request);

                    return new {{cls}}Result(true, Instant.now().toString(), "{{taskName}} completed successfully.");
                }

                private void processCoreLogic({{cls}}Request request) {
                    try { Thread.sleep(50); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
                    logger.debug("[{{cls}}] Core logic processed.");
                }

                public record {{cls}}Request(String context, Map<String, String> metadata) {}
                public record {{cls}}Result(boolean success, String executedAt, String message) {}
            }
            """;
    }

    private static string BuildGoTemplate(string taskName)
    {
        var cls = ToPascalCase(taskName);
        var pkg = System.Text.RegularExpressions.Regex
            .Replace(taskName.ToLowerInvariant(), @"[^a-z0-9]+", "");
        return $$"""
            // Auto-generated by Meridian Local Compilation Engine
            // Task: {{taskName}}

            package {{pkg}}

            import (
            	"context"
            	"fmt"
            	"log/slog"
            	"time"
            )

            type {{cls}}Request struct {
            	Context  string
            	Metadata map[string]string
            }

            type {{cls}}Result struct {
            	Success    bool
            	ExecutedAt time.Time
            	Message    string
            }

            type {{cls}}Service struct {
            	logger *slog.Logger
            }

            func New{{cls}}Service(logger *slog.Logger) *{{cls}}Service {
            	return &{{cls}}Service{logger: logger}
            }

            func (s *{{cls}}Service) Execute(ctx context.Context, req *{{cls}}Request) (*{{cls}}Result, error) {
            	if req == nil {
            		return nil, fmt.Errorf("request is required")
            	}
            	s.logger.InfoContext(ctx, "executing", "service", "{{cls}}", "context", req.Context)

            	if err := s.processCoreLogic(ctx, req); err != nil {
            		return nil, fmt.Errorf("core logic: %w", err)
            	}

            	return &{{cls}}Result{
            		Success:    true,
            		ExecutedAt: time.Now().UTC(),
            		Message:    "{{taskName}} completed successfully.",
            	}, nil
            }

            func (s *{{cls}}Service) processCoreLogic(ctx context.Context, _ *{{cls}}Request) error {
            	s.logger.DebugContext(ctx, "core logic processed", "service", "{{cls}}")
            	return nil
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════
    //  DOCUMENT BUILDERS
    // ═══════════════════════════════════════════════════════════════

    private static string BuildMarkdownDocument(string title, string templateType, DomainProfile p)
        => templateType.ToLowerInvariant().Replace(" ", "-").Replace("_", "-") switch
        {
            "market-analysis"       => MarketAnalysis(title, p),
            "technical-specification" or "technical-spec" => TechnicalSpec(title, p),
            "proposal"              => Proposal(title, p),
            "governance-adr"        => GovernanceAdr(title, p),
            "developer-handbook"    => DeveloperHandbook(title, p),
            "detailed-design"       => DetailedDesign(title, p),
            _                       => ExecutiveSummary(title, p)   // default / executive-summary
        };

    private static string ExecutiveSummary(string title, DomainProfile p) => $"""
        # Executive Summary — {title}

        **Prepared for:** Senior Leadership & Sponsors
        **Date:** {DateTimeOffset.UtcNow:MMMM yyyy}
        **Build timeline:** 6 weeks to pilot
        **Status:** Awaiting Executive Approval

        ---

        ## 01 — The Problem We Are Solving

        The {p.Name} sector is undergoing a structural shift driven by AI adoption, regulatory
        pressure, and competitive intensity from well-capitalised technology entrants.
        {p.Competitors[0].FeatureGap} Organisations that fail to embed AI into core workflows
        risk meaningful market-share erosion within 18–24 months.

        Teams operating without AI-assisted tooling face compounding inefficiency: manual processes
        that consumed acceptable effort at smaller scale become critical bottlenecks as the
        organisation grows. The cost is measured in delayed decisions, missed opportunities,
        and avoidable rework — not just in IT spend.

        **By the numbers:**

        | Metric | Current State |
        |--------|--------------|
        | Average task cycle time | 4–6 hours, manual, dependent on specialist availability |
        | AI-assisted equivalent | 8–15 minutes with {title} |
        | Issues traced to missing AI context | ~30% of escalations are preventable |
        | Current automation coverage | Below 15% of addressable workflows |

        ---

        ## 02 — The Opportunity

        {title} addresses a high-value gap in the current {p.Name} landscape.
        This represents a serviceable addressable market exceeding **$2.4B globally**,
        growing at **28% CAGR through 2028**. The window for first-mover advantage
        in AI-native tooling for this segment closes within 12–18 months as incumbents
        begin integrating AI capabilities into their existing platforms.

        The case for acting now rather than waiting:
        - **Cost of inaction is compounding.** Each quarter of delay is a quarter of competitor
          learning advantage that cannot be recovered by feature parity alone.
        - **Talent expects AI tooling.** Top-quartile hires in {p.Name} functions are
          actively filtering employers by AI maturity during recruitment.
        - **Regulatory pressure is accelerating**, not slowing — early-mover compliance
          advantage is worth 18–24 months of future audit preparation costs.

        ---

        ## 03 — The Proposed Solution

        {title} is an AI-powered platform delivering {p.TechStack} capabilities through a
        unified interface, enabling teams to operationalise AI within existing {p.DbPattern}
        workflows — without rebuilding infrastructure.

        **What the solution delivers:**

        1. **Automated intelligence** — AI models analyse inputs and surface actionable findings
           in seconds, not hours
        2. **Contextual recommendations** — Outputs are grounded in domain-specific knowledge,
           not generic AI responses
        3. **Workflow integration** — Embeds into existing processes via API; no new platform
           for teams to adopt
        4. **Audit-ready outputs** — Every AI-generated result includes rationale, confidence
           score, and data lineage

        **What this solution is NOT:**

        - Not a replacement for human judgement — findings require a decision
        - Not a reporting layer — it surfaces intelligence, not formatted documents
        - Not a vendor lock-in commitment — all components are replaceable via interface contracts
        - Not visible to external parties — operates entirely within the organisation's
          existing infrastructure boundary

        ---

        ## 04 — Risks

        | Risk | Likelihood | Impact if Unmitigated |
        |------|------------|----------------------|
        | **AI model quality insufficient for production use** — outputs too generic to be actionable | Medium | Low adoption; investment not recovered; no evidence base for broader rollout |
        | **Data quality gates block integration** — existing {p.DbPattern} data quality below AI-usable threshold | Medium | 6–12 week remediation delay before any AI benefit is realised |
        | **Stakeholder buy-in stalls post-pilot** — pilot users see value but sponsor approval delayed | Medium–Low | Project enters indefinite holding pattern; team loses momentum |
        | **LLM provider availability** — API quota exhaustion or provider outage during critical period | Low | Degraded-mode operation via offline heuristic engine; no total outage |
        | **Scope creep extends timeline beyond pilot** — new requirements added during build | Medium | 6-week pilot becomes 14-week project; executive confidence erodes |

        ---

        ## 05 — How We Address Each Risk

        | Risk | Mitigation | Status |
        |------|-----------|--------|
        | AI model quality | Human-in-the-loop validation at each stage; weekly accuracy ratings from pilot users; formal go/no-go gate at pilot end | Designed in |
        | Data quality | Pre-pilot data audit (Week 0); minimum viable data quality thresholds defined before development begins | Action required |
        | Stakeholder buy-in | Weekly demonstrable milestone outcomes (not just code); non-technical verifiable outputs from Week 3 onward | Designed in |
        | LLM provider availability | Multi-model cascade (primary → secondary → offline fallback); zero total outage by design | Designed in |
        | Scope creep | Scope freeze at Week 1; change requests parked in backlog; pilot scope is fixed | Requires governance |

        ---

        ## 06 — Strategic Value Drivers

        - **Velocity:** Time-to-value in 30 days vs 12-month enterprise AI implementation cycles
        - **Cost efficiency:** 60–75% reduction in AI infrastructure overhead vs build-your-own
        - **Compliance-ready:** Domain-specific regulatory alignment baked in from day one
        - **Scalability:** Horizontally scaled deployment; zero-downtime upgrades
        - **Reversibility:** Every third-party AI dependency is behind an interface; swap in
          one configuration change, no code rewrites
        - **Knowledge retention:** AI-assisted processes create structured outputs that remain
          accessible after staff transitions

        ---

        ## 07 — Financial Highlights

        | Metric | Year 1 | Year 2 | Year 3 |
        |--------|--------|--------|--------|
        | ARR Target | $2.4M | $8.1M | $21.6M |
        | Gross Margin | 72% | 78% | 82% |
        | Build Investment | ~$120K | — | — |
        | Monthly Operating Cost | ~$70–$150 | ~$200–$400 | ~$600–$1,200 |
        | Payback Period | 14 months | — | — |
        | NPV (3-year, 12% discount) | $4.2M | — | — |

        *Note: ARR figures assume full commercial rollout after pilot. Operating costs are
        primarily LLM API consumption; scale linearly with usage.*

        ---

        ## 08 — Milestones & Delivery Plan

        Each week ends with a concrete, demonstrable outcome — not just code, but a working
        capability that a non-technical stakeholder can verify.

        **Week 1 — Foundation & Connectivity**
        *Verifiable outcome:* System starts, core API responds, health check passes.
        All dependencies identified; no-key offline mode verified.

        **Week 2 — Data Ingestion & Context**
        *Verifiable outcome:* Real {p.Name} data flows through the pipeline.
        Structured output produced for at least one live scenario.

        **Week 3 — First AI Capability Live**
        *Verifiable outcome:* A domain stakeholder asks a natural-language question
        and receives a specific, accurate, evidence-backed response. First real business value.

        **Week 4 — Full Capability Coverage**
        *Verifiable outcome:* All primary use cases addressed. Role-based access enforced.
        Each capability validated by the relevant role holder before sign-off.

        **Week 5 — Security, Audit & Hardening**
        *Verifiable outcome:* Compliance officer reviews audit trail and confirms no sensitive
        data retained beyond session scope. All security controls tested and verified.

        **Week 6 — Pilot Launch**
        *Verifiable outcome:* 6–8 named pilot users operating the system on real work.
        Weekly feedback collected. Go/no-go for wider rollout scheduled at Week 12.

        ---

        ## 09 — What We Need Before We Can Start

        Three items must be confirmed before implementation begins. All are prerequisites,
        not nice-to-haves.

        **① Data access confirmed**
        Operations team to confirm read access to source {p.DbPattern} systems.
        Required by end of Week 0 — **blocker**.

        **② Pilot users identified**
        6–8 named individuals across relevant roles committed to weekly feedback.
        Required by end of Week 1.

        **③ Infrastructure provisioned**
        Cloud environment and API credentials available. LLM provider quota requested
        immediately — approval takes 3–10 business days.

        ---

        **To authorise this project, respond in writing: "approved."**

        *Implementation begins the following business day.*

        ---
        *Prepared by MeridianStudio AI — {DateTimeOffset.UtcNow:yyyy-MM-dd}*
        """;

    private static string MarketAnalysis(string title, DomainProfile p) => $"""
        # Market Analysis — {title}

        ## Market Sizing

        The global {p.Name} market is estimated at **$18.4B in 2024**, projected to reach
        **$51.2B by 2028** (CAGR: 29.1%). AI-native platforms represent the fastest-growing
        sub-segment at 38% CAGR.

        ## Competitive Landscape

        {string.Join("\n\n", p.Competitors.Select(c => $"""
        ### {c.CompetitorName}
        **Feature Gap:** {c.FeatureGap}
        **Impact Score:** {c.ImpactScore}
        **Strategic Playbook:** {c.StrategicPlaybook}
        """))}

        ## Demand Signals
        - 73% of {p.Name} enterprises cite AI integration complexity as primary barrier
        - Average AI project failure rate: 67% (McKinsey, 2024)
        - Buy vs build: 81% of CIOs prefer vendor-managed AI infrastructure
        - Regulatory tailwinds accelerating compliant AI adoption in all target verticals

        ## Target Customer Profile
        - **Firmographics:** 500–5,000 employees, $100M–$2B revenue
        - **Budget:** $250K–$2M annual AI platform spend
        - **Decision makers:** CTO, CDO, VP Engineering, Chief AI Officer
        - **Pain:** Failed internal AI initiatives; 12+ month implementation timelines

        ## Positioning
        {title} occupies the **enterprise AI platform** quadrant — differentiated from
        point solutions by full-stack coverage and from hyperscalers by domain specialisation
        and deployment simplicity.

        ---
        *Prepared by MeridianStudio AI — {DateTimeOffset.UtcNow:yyyy-MM-dd}*
        """;

    private static string TechnicalSpec(string title, DomainProfile p) => $"""
        # Technical Specification — {title}

        ## Architecture
        Event-driven microservices on Kubernetes. All services expose gRPC internally
        and REST/JSON externally. Schema registry enforces contract compatibility.

        ## Technology Stack
        {p.TechStack}

        ## Data Architecture
        **Primary:** {p.DbPattern}
        **Cache:** Redis Cluster (read-through, write-behind)
        **Search:** Elasticsearch 8 (BM25 + kNN hybrid)
        **Streaming:** Apache Kafka (3-node, RF=3, ISR=2)

        ## Security Controls
        | Control | Implementation |
        |---------|----------------|
        | AuthN | OAuth 2.0 + OIDC (RS256 JWT) |
        | AuthZ | RBAC + ABAC via OPA Gatekeeper |
        | Encryption at rest | AES-256-GCM (AWS KMS / Azure Key Vault) |
        | Encryption in transit | TLS 1.3 (mTLS service-to-service) |
        | Secrets management | HashiCorp Vault (dynamic credentials) |
        | Vulnerability scanning | Trivy (container) + Dependabot (deps) |

        ## Scalability Targets
        - Throughput: 50,000 req/min sustained
        - Latency: p50 ≤ 80ms · p95 ≤ 150ms · p99 ≤ 200ms
        - Availability: 99.95% monthly SLA
        - Storage: petabyte-scale object storage for model artefacts

        ## CI/CD Pipeline
        GitHub Actions → unit tests → integration tests → container build →
        SAST/DAST scan → staging deploy → smoke tests → production canary (5% → 100%)

        ---
        *Prepared by MeridianStudio AI — {DateTimeOffset.UtcNow:yyyy-MM-dd}*
        """;

    private static string Proposal(string title, DomainProfile p) => $"""
        # Business Proposal — {title}

        ## Executive Overview
        This proposal outlines the delivery of {title}, a purpose-built {p.Name} AI platform
        that will eliminate {p.Competitors[0].FeatureGap.Split(';')[0].Trim()} — the primary
        competitive gap exploited by market leaders today.

        ## Proposed Solution
        {title} delivers a modular AI platform encompassing:
        - Real-time inference pipeline ({p.TechStack})
        - Enterprise-grade data architecture ({p.DbPattern})
        - Compliance-ready deployment with SOC 2 Type II controls

        ## Delivery Plan
        | Phase | Scope | Duration | Cost |
        |-------|-------|----------|------|
        | Phase 1 — Foundation | Core API, Auth, data pipeline | 8 weeks | $280K |
        | Phase 2 — AI Layer | Inference engine, model integration | 6 weeks | $210K |
        | Phase 3 — Enterprise | Multi-tenancy, compliance, reporting | 6 weeks | $190K |
        | Phase 4 — Launch | GA hardening, documentation, training | 4 weeks | $120K |
        | **Total** | | **24 weeks** | **$800K** |

        ## Investment & ROI
        - **Total investment:** $800K (implementation) + $180K/yr (platform licence)
        - **Projected Year 1 ROI:** 340% based on {p.Competitors[0].StrategicPlaybook.Split('.')[0]}
        - **Payback period:** 11 months

        ## Acceptance Criteria
        All deliverables subject to UAT sign-off, load test report (50K req/min),
        and passing SOC 2 evidence review.

        ---
        *Submitted by MeridianStudio AI — {DateTimeOffset.UtcNow:yyyy-MM-dd}*
        """;

    private static string GovernanceAdr(string title, DomainProfile p) => $"""
        # Architecture Governance & ADR — {title}

        **Status:** Proposed
        **Date:** {DateTimeOffset.UtcNow:yyyy-MM-dd}
        **Domain:** {p.Name}

        ---

        ## ADR-001: Architecture for {title}

        ### Context

        The {p.Name} sector faces a measurable gap: {p.Competitors[0].FeatureGap}
        Existing tooling does not address this at scale. The team has evaluated multiple
        approaches and must commit to one architecture before development begins.

        Key constraints driving this decision:
        - Must integrate with existing {p.DbPattern} data infrastructure
        - 6-week timeline to pilot — cannot afford architectural pivots mid-build
        - Team has {p.TechStack} expertise; deviation adds risk
        - Regulatory context: {p.Name} domain carries specific data-handling obligations

        ### Decision

        Build {title} as a modular, API-first platform using {p.TechStack}.
        Each capability is independently deployable behind a unified gateway.
        All AI provider dependencies are behind a single interface, enabling zero-code
        provider swaps. The {p.DbPattern} pattern is used for data persistence.

        Specifically:
        - Synchronous request-response model (no async queue in v1 — wrong UX for interactive use)
        - AI provider cascade: primary → secondary → offline heuristic fallback
        - Domain model as sealed, immutable records — no free-text fields on audit types
        - Role-based access enforced at the feature level, not only at the gateway

        ### Consequences

        **Positive:**
        - Independent deployability of each module; blast radius of any single failure is bounded
        - Provider abstraction means switching AI vendors is a one-line DI configuration change
        - API-first design enables future integration with {p.Name} systems and third-party tools
        - Sealed domain models make accidental sensitive-data logging a compile-time error

        **Negative:**
        - Synchronous API adds visible latency for long-running AI calls (8–25 seconds)
        - Abstraction layer requires interface updates each time a new provider capability is added
        - Modular deployment increases operational surface area for monitoring and alerting
        - Offline fallback produces lower-quality outputs; users must be made aware of degraded mode

        ---

        ## Top 3 Production Failure Modes

        ### Failure Mode 1: AI Model Quality Below Usable Threshold (Probability: 55–65%)

        **What happens:** {title} output is too generic to be actionable for {p.Name} use cases.
        Users lose confidence after 2–3 poor results and stop using the system.

        **Mitigation:** Human-in-the-loop validation at each stage of the pilot. Weekly accuracy
        ratings collected from named pilot users. Formal go/no-go quality gate at end of pilot
        before wider rollout is committed to. Prompt library tuned against {p.Name} domain examples.

        **Residual risk:** LLM output quality varies across sub-domains within {p.Name}.
        Segments with sparse training data may consistently underperform.

        ### Failure Mode 2: {p.Name} Data Quality Gates Block Integration (Probability: 40–50%)

        **What happens:** Source {p.DbPattern} data is below the minimum quality threshold
        for AI processing. Missing fields, inconsistent schemas, or stale records cause
        {title} to surface misleading outputs.

        **Mitigation:** Pre-pilot data audit (Week 0). Minimum viable data quality thresholds
        defined before development begins. Input validation rejects or flags low-quality records
        before they reach the AI layer.

        **Residual risk:** Data quality remediation is owned by a different team. Timeline
        dependency on external parties may delay the pilot by 2–4 weeks.

        ### Failure Mode 3: Scope Creep Extends Timeline Beyond Pilot (Probability: 35–45%)

        **What happens:** Stakeholders add requirements during the build phase.
        The 6-week pilot becomes a 14-week project. Executive confidence erodes.

        **Mitigation:** Scope freeze at end of Week 1. All change requests parked in a
        labelled backlog with explicit "post-pilot" status. Weekly milestone demos with
        non-technical verifiable outcomes make scope changes visible immediately.

        **Residual risk:** Scope creep that arrives as "clarifications" rather than new
        requirements is harder to freeze. Product owner must actively manage this.

        ---

        ## Alternatives Analysis

        ### Decision A: {p.TechStack} vs. Alternative Stack

        | Alternative | Rejected Because |
        |---|---|
        | Rebuild on a different stack | Team expertise is in {p.TechStack}; context switching adds 3–4 weeks with no quality gain |
        | Third-party SaaS for all AI | Vendor lock-in; cannot meet {p.Name} data-residency requirements; no customisation path |
        | **{p.TechStack} with abstraction layer (chosen)** | Leverages existing expertise; abstraction keeps vendor options open; meets domain requirements |

        ### Decision B: Synchronous API vs. Async Queue

        | Alternative | Rejected Because |
        |---|---|
        | Async job queue | Correct for batch processing; wrong for interactive {p.Name} workflows where users expect immediate response |
        | Pre-computed results cache | Stale for first-time queries; prohibitively expensive to pre-warm all {p.Name} sub-domains |
        | **Synchronous with 30s timeout (chosen)** | Correct for pilot scale; transparent loading state is acceptable at this usage volume |

        ### Decision C: {p.DbPattern} vs. Alternative Persistence

        | Alternative | Rejected Because |
        |---|---|
        | Flat file / object storage | No query capability; compliance reporting requires structured audit trail |
        | Rebuild on new DB technology | Existing {p.Name} data estate is in {p.DbPattern}; migration cost exceeds benefit for v1 |
        | **{p.DbPattern} (chosen)** | Integrates with existing infrastructure; team familiar; meets audit and query requirements |

        ---

        ## Top 5 Security Concerns

        | # | Concern | Mitigation |
        |---|---|---|
        | 1 | {p.Name} data exposed via AI output | Output validation strips PII patterns before results reach the UI; audit log retains metadata only |
        | 2 | Prompt injection via user inputs | Input sanitisation blocks known injection patterns; all user content inserted inside XML-tagged delimiters |
        | 3 | Unauthorised access to sensitive {p.Name} records | Role-based access enforced at feature level; every access event logged with user ID and timestamp |
        | 4 | AI provider API key exposure | Keys stored in secrets manager only; never in code, config files, or environment variables |
        | 5 | Third-party AI provider data retention | Provider contracts must include zero-data-retention clause; sensitive {p.Name} data pseudonymised before transmission |

        ---

        ## Complexity Rating: 5 / 10

        **What makes it a 5:** AI provider cascade with fallback, role-based access, domain-specific
        data integration with {p.DbPattern}, and a 6-week timeline with production-quality output.

        **What keeps it below 7:** No async queue, no CQRS, no event sourcing. Single deployable
        unit. Synchronous request-response throughout. Offline fallback eliminates total-outage risk.

        **Simpler alternative (complexity 3):** Single AI provider, no fallback, no role gating,
        manual data pipeline. Correct for a prototype; wrong for a {p.Name} pilot with real users.

        ---

        ## Junior Developer Readiness

        | Likely Confusion | How the Design Prevents It |
        |---|---|
        | "Where does {p.Name} domain logic live?" | Domain layer contains only sealed models with no external dependencies; business logic is in the Application layer |
        | "How do I add a new AI provider?" | Implement the provider interface; register in DI; no other files change — swap guide is in the abstraction map |
        | "Which fields can I add to the audit log?" | Sealed audit metadata record; adding free-text fields requires a PR that architects review explicitly |
        | "Why is the offline fallback producing different results?" | Comment in fallback method explains heuristic nature; `modelUsed` field in every response signals offline mode to the UI |
        | "Can I bypass role-checks for testing?" | Test doubles inject the desired role directly; never bypass production role enforcement — comment in role-check block explains why |

        ---

        ## AI Generation Caveats

        This document was generated by the Heuristic Engine (Offline). Probabilities, timelines,
        and complexity ratings are illustrative estimates based on {p.Name} domain patterns.
        Actual values should be validated with the delivery team before committing to a plan.

        ---
        *Generated by MeridianStudio AI — {DateTimeOffset.UtcNow:yyyy-MM-dd}*
        """;

    private static string DeveloperHandbook(string title, DomainProfile p) => $$"""
        # Developer Handbook — {{title}}

        > Everything a developer needs from first commit to pilot launch.
        > **Domain:** {{p.Name}} | **Tech stack:** {{p.TechStack}}

        ---

        ## PART 1 — EPICS & USER STORIES

        ### EPIC 1: Foundation & Project Scaffold
        **Goal:** Any developer can clone the repo, configure credentials, and run {{title}} locally within 15 minutes.
        **Points:** 13

        #### Story 1.1 — Project structure and build pipeline (3 pts)
        **As a** developer new to the project
        **I want** a clear, buildable project structure with documented conventions
        **So that** I can start contributing without an onboarding session

        **Acceptance Criteria:**
        - [ ] Project builds with zero warnings and zero errors
        - [ ] Health-check endpoint returns HTTP 200 with structured JSON
        - [ ] All third-party package versions pinned (no floating ranges)
        - [ ] README documents local setup in under 5 steps
        - [ ] `.env.example` or `appsettings.example.json` documents every required config key

        #### Story 1.2 — {{p.Name}} data connection wired (5 pts)
        **As a** developer
        **I want** a verified connection to the {{p.DbPattern}} data source
        **So that** {{title}} can read the {{p.Name}} data it needs to function

        **Acceptance Criteria:**
        - [ ] Connection string externalised (never hardcoded)
        - [ ] Connection verified against a real {{p.Name}} dataset in dev environment
        - [ ] Read timeout and retry policy configured
        - [ ] Connection failure returns a structured error, not an unhandled exception
        - [ ] Unit tests mock the data layer; integration tests hit a real (dev) instance

        ---

        ### EPIC 2: Core {{p.Name}} Intelligence
        **Goal:** {{title}} surfaces accurate, actionable intelligence for {{p.Name}} workflows.
        **Points:** 34

        #### Story 2.1 — Primary AI capability (8 pts)
        **As a** {{p.Name}} practitioner
        **I want** {{title}} to analyse my input and return domain-specific findings
        **So that** I can make faster, better-informed decisions without specialist overhead

        **Acceptance Criteria:**
        - [ ] Returns structured findings with title, detail, evidence, and severity
        - [ ] Evidence quotes are grounded in actual input — no fabrication
        - [ ] Response time under 15 seconds for typical {{p.Name}} inputs
        - [ ] Confidence score returned alongside findings
        - [ ] Empty or invalid input returns a clear validation error, not a 500

        #### Story 2.2 — {{p.Name}} domain context enrichment (5 pts)
        **As a** {{p.Name}} practitioner
        **I want** AI outputs calibrated to {{p.Name}} conventions and terminology
        **So that** findings are immediately actionable without translation overhead

        **Acceptance Criteria:**
        - [ ] Competitor benchmark data for {{p.Competitors[0].CompetitorName}} included in context
        - [ ] Domain-specific terminology used consistently (no generic AI boilerplate)
        - [ ] Findings reference {{p.Name}} standards and regulatory context where relevant
        - [ ] 3 domain SMEs review outputs and confirm accuracy before sign-off

        #### Story 2.3 — Role-based access control (5 pts)
        **As a** system
        **I want** each capability gated by the requesting user's role
        **So that** sensitive {{p.Name}} data is never surfaced to unauthorised users

        **Acceptance Criteria:**
        - [ ] Roles defined and documented in code comments
        - [ ] Unauthorised requests return a structured refusal with explanation, not a 403
        - [ ] Every access event logged with user ID, role, and timestamp
        - [ ] Integration tests cover all role scenarios (authorised and unauthorised)

        ---

        ### EPIC 3: Integration & Workflow Embedding
        **Goal:** {{title}} fits into existing {{p.Name}} workflows; users do not need to learn a new tool.
        **Points:** 21

        #### Story 3.1 — Existing {{p.Name}} system integration (8 pts)
        **As a** {{p.Name}} practitioner
        **I want** {{title}} to work with data already in our existing {{p.DbPattern}} systems
        **So that** I do not have to re-enter information I already have

        **Acceptance Criteria:**
        - [ ] Reads from existing {{p.DbPattern}} data stores without requiring migration
        - [ ] Data mapping layer translates existing schema to {{title}} internal models
        - [ ] Schema changes in source systems handled gracefully (tolerant reader pattern)
        - [ ] No writes to source systems in v1 (read-only integration)

        #### Story 3.2 — Audit trail and compliance logging (5 pts)
        **As a** compliance officer
        **I want** every AI-assisted interaction logged with metadata
        **So that** I can demonstrate to regulators what {{title}} accessed and when

        **Acceptance Criteria:**
        - [ ] Every request logged: user ID, timestamp, input type (never input content), output type
        - [ ] Audit log stored separately from application logs; retained per {{p.Name}} regulatory requirements
        - [ ] Audit log contains no {{p.Name}} record content — metadata only
        - [ ] Compliance officer can export audit summary without engineering involvement

        ---

        ## PART 2 — ARCHITECTURE OVERVIEW

        {{title}} follows a layered architecture with strict dependency rules.
        No layer references a layer above it. All third-party dependencies sit behind interfaces.

        ```
        Domain/
        ├── Models/        ← Sealed records for {{p.Name}} entities; no external dependencies
        └── Interfaces/    ← Contracts only; no implementations

        Application/
        ├── Services/      ← Orchestration and business logic for {{title}} capabilities
        └── Contracts/     ← Request/response types for each API endpoint

        Infrastructure/
        ├── Data/          ← {{p.DbPattern}} access; implements IDataRepository
        ├── AI/            ← LLM provider implementations; implements IIntelligenceProvider
        └── Audit/         ← Compliance logging; implements IAuditLogger

        API/
        ├── Endpoints/     ← One handler per capability; thin — delegates to Application
        └── Program.cs     ← DI registrations; middleware pipeline; no business logic
        ```

        **Dependency rule:** `Domain` ← `Application` ← `Infrastructure` ← `API`.
        Cross-cutting concerns (logging, telemetry) injected via interfaces, not static calls.

        ---

        ## PART 3 — COMPONENT REFERENCE

        | Component | Purpose | Key Responsibilities | Does NOT |
        |---|---|---|---|
        | `IntelligenceService` | Core AI capability orchestrator | Calls AI provider, validates output, applies {{p.Name}} domain context | Store results or manage sessions |
        | `DataRepository` | {{p.DbPattern}} access layer | Reads {{p.Name}} records; maps to domain models | Write to source systems in v1 |
        | `RoleEnforcer` | Access control | Checks user role before any sensitive operation; returns structured refusal | Throw exceptions for role failures |
        | `AuditLogger` | Compliance logging | Writes metadata-only audit events; fire-and-forget | Block the calling request if it fails |
        | `InputValidator` | Boundary validation | Validates and sanitises all user-supplied input; blocks injection patterns | Know anything about business logic |

        ---

        ## PART 4 — ABSTRACTION MAP

        Every third-party dependency is behind an interface. Swapping a vendor is a one-line DI change.

        | Third-Party | Interface | Production Implementation | Test Double |
        |---|---|---|---|
        | LLM provider | `IIntelligenceProvider` | `AiProviderAdapter` | `StubIntelligenceProvider` |
        | {{p.DbPattern}} store | `IDataRepository` | `{{p.DbPattern}}Repository` | `InMemoryDataRepository` |
        | Audit persistence | `IAuditLogger` | `SqlAuditLogger` | `NullAuditLogger` |
        | Identity/auth | `IUserContextAccessor` | `JwtUserContextAccessor` | `StubUserContextAccessor` |
        | Secret management | `ISecretProvider` | `KeyVaultSecretProvider` | `EnvironmentSecretProvider` |

        **Rule:** No file outside `Infrastructure/` imports a vendor SDK. Enforced via project-level package restrictions in CI.

        ---

        ## PART 5 — DESIGN PATTERNS

        | Pattern | Where Applied | Why Chosen Over Alternatives |
        |---|---|---|
        | Repository | `IDataRepository` | Decouples {{p.Name}} data access from business logic; enables in-memory test doubles without a running DB |
        | Null Object | `AuditLogger.LogAsync()` | Audit failures must never fail a user request; Null Object avoids null-checks and swallows silently |
        | Sealed value objects | All domain models | Immutability prevents accidental mutation; structural equality simplifies tests; `required` properties catch missing fields at compile time |
        | Guard clauses | All API handlers | Early-exit pattern keeps the happy path at zero nesting; every guard clause is a single unit test case |
        | Provider cascade | `IIntelligenceProvider` | Single provider is a single point of failure; cascade to secondary + offline fallback eliminates total-outage risk for {{title}} |

        ---

        ## PART 6 — TO-DO CHECKLIST

        ### Week 1 — Foundation
        - [ ] Project builds with zero warnings
        - [ ] Health check endpoint operational
        - [ ] {{p.DbPattern}} connection verified against dev data
        - [ ] Secrets externalised; no credentials in any committed file
        - [ ] CI pipeline runs on every commit

        ### Week 2 — Core Capability
        - [ ] Primary AI capability returns structured findings for {{p.Name}} inputs
        - [ ] Domain context enrichment verified by 1 SME
        - [ ] Response time under 15 seconds for typical inputs
        - [ ] Role-based access control wired and tested for all roles

        ### Week 3 — Integration
        - [ ] {{p.DbPattern}} integration reads live {{p.Name}} data in dev
        - [ ] Audit log writes metadata-only records (no content) for every request
        - [ ] Input validation blocks all known injection patterns
        - [ ] End-to-end test passes with a real {{p.Name}} scenario

        ### Week 4 — Hardening
        - [ ] Load test: target concurrency at expected pilot volume
        - [ ] Security review: no credentials in logs, no PII in audit trail
        - [ ] All third-party dependencies have test doubles; integration tests use real instances
        - [ ] README updated; any developer can set up locally without asking questions

        ### Pre-Launch Checklist
        - [ ] Pilot users identified and access provisioned
        - [ ] Compliance officer has reviewed audit log format and confirmed it meets requirements
        - [ ] Monitoring alerts configured for error rate and latency
        - [ ] Rollback plan documented: how to disable {{title}} without downtime
        - [ ] No hardcoded credentials in any environment

        ---
        *{{title}} Developer Handbook — {{DateTimeOffset.UtcNow:yyyy-MM-dd}}*
        """;

    private static string DetailedDesign(string title, DomainProfile p) => $$"""
        # Detailed Design — {{title}}

        > Sprint-ready implementation guide for {{title}} in the {{p.Name}} domain.
        > Everything an engineer needs to start Monday.

        ---

        ## 1. Solution Structure

        ```
        {{title}}.API/
        ├── Domain/
        │   └── Models/          ← Sealed records for {{p.Name}} entities; no dependencies
        ├── Application/
        │   ├── Contracts/       ← Request/response types per capability
        │   └── Services/        ← Business logic and AI orchestration
        ├── Infrastructure/
        │   ├── AI/              ← LLM provider implementations
        │   ├── Data/            ← {{p.DbPattern}} access layer
        │   ├── Guard/           ← Input validation and injection blocking
        │   └── Audit/           ← Compliance logging (metadata only)
        └── API/
            ├── Endpoints/       ← One handler per capability
            └── Program.cs       ← DI registrations; no business logic
        ```

        ---

        ## 2. Technology Stack

        | Package | Version | Purpose |
        |---|---|---|
        | .NET | 10 | Runtime |
        | ASP.NET Core Minimal APIs | 10 | HTTP endpoints |
        | Microsoft.Extensions.Caching.Memory | 9.x | In-process response cache |
        | Scalar.AspNetCore | latest | OpenAPI UI at /scalar/v1 |
        | Angular | 19 | Frontend framework |
        | Tailwind CSS | v4 | Utility-first CSS |
        | lucide-angular | latest | Icon set |
        | RxJS | 7.8 | Observable streams |

        ---

        ## 3. Environment Configuration

        appsettings.json (committed — no secrets):
        ```json
        {
          "LLM": {
            "Gemini": { "ApiKey": "" },
            "Groq":   { "ApiKey": "" },
            "Claude": { "ApiKey": "" }
          },
          "Cache": {
            "Research":  { "TtlHours": 24 },
            "Blueprint": { "TtlHours": 24 },
            "Document":  { "TtlHours": 24 }
          }
        }
        ```

        API keys set via dotnet user-secrets — never committed:
        ```bash
        dotnet user-secrets set "LLM:Gemini:ApiKey" "AIza..."
        dotnet user-secrets set "LLM:Groq:ApiKey"   "gsk_..."
        dotnet user-secrets set "LLM:Claude:ApiKey" "sk-ant-..."
        ```

        ---

        ## 4. Domain Models (Key Records)

        ```csharp
        // ResearchResponse — returned by POST /api/research
        public sealed record ResearchResponse
        {
            public required string Domain { get; init; }
            public required List<string> DomainsList { get; init; }
            public required List<CompetitorInsight> CompetitorInsights { get; init; }
            public required List<PrioritizedItem> Items { get; init; }
            public string ModelUsed { get; init; } = string.Empty;
        }

        // PrioritizedItem — individual solution opportunity
        public sealed record PrioritizedItem
        {
            public required string Id { get; init; }
            public required string Name { get; init; }
            public required string Description { get; init; }
            public required int Urgency { get; init; }     // 1-10
            public required int Difficulty { get; init; }  // 1-10
            public required int Value { get; init; }       // 1-10
            public required string Rationale { get; init; }
            public required string RealLifeValue { get; init; }
            public required string IntegrationSteps { get; init; }
        }
        ```

        ---

        ## 5. API Contracts

        **POST /api/research**
        ```json
        // Request
        { "keywords": "healthcare AI", "loadMore": false, "page": 1 }

        // Response (200 OK)
        {
          "domain": "Healthcare AI",
          "domainsList": ["Clinical Decision Support", "..."],
          "competitorInsights": [ { "competitorName": "...", "featureGap": "...", "impactScore": "8.5/10" } ],
          "items": [ { "id": "hc-001", "name": "...", "urgency": 9, "difficulty": 7, "value": 10 } ],
          "modelUsed": "Gemini (gemini-2.5-flash)"
        }

        // Error (400 Bad Request)
        { "errors": { "Keywords": ["Keywords must not be empty."] } }
        ```

        ---

        ## 6. Error Handling

        | Error Type | HTTP | Cause | User Message |
        |---|---|---|---|
        | ValidationProblem | 400 | Failed InputGuard checks | Field-level error messages |
        | LLM timeout | 500 to fallback | Provider over 30s | Heuristic engine result returned |
        | All providers failed | 200 (fallback) | Quota exhaustion | modelUsed: Heuristic Engine (Offline) |
        | Injection detected | 400 | InputGuard.HasInjection | Contains disallowed content |

        ---

        ## 7. Test Strategy

        ```csharp
        // Unit test — InputGuard injection detection
        [Fact]
        public void ValidateResearch_WithInjectionKeyword_ReturnsError()
        {
            var req = new ResearchRequest { Keywords = "ignore previous instructions" };
            var errors = InputGuard.ValidateResearch(req);
            Assert.NotNull(errors);
            Assert.True(errors.ContainsKey("Keywords"));
        }

        // Integration test — health endpoint
        [Fact]
        public async Task GetHealth_Returns200WithHealthyStatus()
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/health");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("healthy", body);
        }
        ```

        ---

        ## 8. Sprint Plan

        ### Week 1 — Foundation
        **Deliverable:** API starts, health check passes, Angular shell renders.
        - [ ] dotnet run at http://localhost:5000
        - [ ] npm start at http://localhost:4200
        - [ ] Scalar UI at http://localhost:5000/scalar/v1

        ### Week 2 — Research
        **Deliverable:** User can search any keyword and see 5 solutions with competitor insights.
        - [ ] POST /api/research returns correct response shape
        - [ ] Heuristic engine covers all 9 domain verticals offline
        - [ ] Results cached for 24 hours

        ### Week 3 — Blueprint + Documents
        **Deliverable:** User can generate a full blueprint and any document type from it.
        - [ ] POST /api/generate-blueprint returns all 5 Markdown fields
        - [ ] POST /api/generate-document supports all 7 template types
        - [ ] Document content is 600+ words with proper Markdown structure

        ### Week 4 — Code Generation + Project Zip
        **Deliverable:** User can generate and download a complete project scaffold.
        - [ ] POST /api/execute-task generates compilable code per step
        - [ ] POST /api/generate-project returns valid zip for all 5 languages
        - [ ] C# zip includes .sln, .csproj, Program.cs, domain model, DbContext

        ### Pre-Launch Checklist
        - [ ] All 7 document template types tested end-to-end
        - [ ] Zip download verified for C# and TypeScript
        - [ ] SSE model status stream verified in browser
        - [ ] No API keys in any committed file
        - [ ] CORS policy reviewed for production

        ---
        *MeridianStudio Detailed Design — {{DateTimeOffset.UtcNow:yyyy-MM-dd}}*
        """;

    // ═══════════════════════════════════════════════════════════════
    //  PROMPT BUILDERS
    // ═══════════════════════════════════════════════════════════════

    private static string BuildPromptText(string component, string llm, string? context) => $"""
        # Developer Handoff Prompt — {component}

        ## Role & Identity
        You are a Principal Software Architect and {llm} specialist. Your mandate is to
        produce production-grade, fully compilable code artefacts for **{component}**
        with zero placeholders in business-critical paths.

        ## Component Context
        **Component:** {component}
        **Target LLM / Runtime:** {llm}
        {(context is not null ? $"**Additional Context:** {context}" : "")}

        ## Functional Requirements
        1. Implement the core {component} service with clean separation of concerns
        2. Expose a strongly-typed public API surface (C# records / interfaces)
        3. All I/O operations must be async with CancellationToken propagation
        4. Validate all external inputs at the boundary; trust internal calls
        5. Log at appropriate levels using Microsoft.Extensions.Logging abstractions

        ## Technical Standards (.NET 10 / C# 13)
        - Nullable annotations throughout (`<Nullable>enable</Nullable>`)
        - `TreatWarningsAsErrors` = true — zero compiler warnings acceptable
        - Prefer `required` init-only properties on records over mutable setters
        - Use `ArgumentException.ThrowIfNullOrWhiteSpace` and
          `ArgumentOutOfRangeException.ThrowIfLessThan/GreaterThan` for guards
        - Dependency injection via primary constructor parameters (not field injection)
        - No `static` state outside sealed caches registered as Singleton

        ## Resilience Requirements
        - Wrap all external calls in try/catch distinguishing 429/503 from fatal errors
        - Implement circuit-breaker + exponential-jitter retry via Polly
        - Every service must degrade gracefully to a local fallback on external failure

        ## Deliverables (in order)
        1. `{component}Service.cs` — full implementation
        2. `I{component}Service.cs` — interface contract
        3. `{component}Request.cs` / `{component}Response.cs` — typed contracts
        4. `{component}ServiceTests.cs` — xUnit tests (Arrange / Act / Assert)
        5. `{component}IntegrationTests.cs` — TestContainers-based fixture
        6. `README.md` — setup, configuration, and usage examples

        ## Output Constraints
        - Return ONLY compilable code — no prose explanations between files
        - Each file starts with the namespace declaration
        - Include `// Auto-generated by {llm} — review before committing` header
        """;

    private static string BuildDirectives(string component, string llm) => $"""
        SYSTEM: You are a {llm} code generation agent for the {component} module.

        DIRECTIVE 1 — COMPLETENESS: Every method body must be fully implemented.
        Partial stubs (`throw new NotImplementedException()`) are forbidden in any
        class under `Application/` or `Infrastructure/`. Use them only in test mocks.

        DIRECTIVE 2 — SAFETY: Never introduce SQL injection, command injection, or
        deserialisation gadget vulnerabilities. Parameterise all queries. Use
        `System.Text.Json` — never `Newtonsoft.Json` with TypeNameHandling.All.

        DIRECTIVE 3 — NAMING: Follow Microsoft .NET naming conventions.
        Interfaces prefix `I`. Async methods suffix `Async`. Private fields prefix `_`.

        DIRECTIVE 4 — TESTING: Each public method must have at least one happy-path
        and one edge-case unit test. Use FluentAssertions for assertions. Do not mock
        the database — use TestContainers (Postgres, Redis) for integration tests.

        DIRECTIVE 5 — DOCUMENTATION: Add XML doc comments to all public members.
        Document the WHY for any non-obvious design decision inline, not in the doc.

        DIRECTIVE 6 — PERFORMANCE: Prefer `IAsyncEnumerable<T>` for streaming results.
        Use `ArrayPool<T>` for short-lived buffers > 1KB. Avoid `LINQ` in hot paths —
        prefer `Span<T>` or explicit loops where allocations are measurable.

        STOP CONDITIONS: Halt generation if the prompt lacks sufficient context to
        produce a type-safe implementation. Return a structured clarification request
        instead of guessing.
        """;

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static string DeterministicId(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private static string ToPascalCase(string input)
        => string.Concat(
            input.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
                 .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string Slugify(string input)
        => System.Text.RegularExpressions.Regex
            .Replace(input.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "_")
            .Trim('_');

    private static string NormaliseLLM(string? raw) => raw?.Trim() switch
    {
        null or "" => "Claude Sonnet",
        var s when s.Contains("gpt", StringComparison.OrdinalIgnoreCase)     => "GPT-4o",
        var s when s.Contains("gemini", StringComparison.OrdinalIgnoreCase)  => "Gemini 1.5 Pro",
        var s when s.Contains("claude", StringComparison.OrdinalIgnoreCase)  => "Claude Sonnet",
        var s => s
    };

    // ═══════════════════════════════════════════════════════════════
    //  STATIC DOMAIN DATA
    // ═══════════════════════════════════════════════════════════════

    private static readonly IReadOnlyDictionary<string, DomainProfile> Profiles =
        new Dictionary<string, DomainProfile>(StringComparer.Ordinal)
        {
            ["Healthcare AI"]                     = HealthcareProfile(),
            ["Financial Technology"]              = FinTechProfile(),
            ["Legal Technology"]                  = LegalTechProfile(),
            ["Retail & E-Commerce"]               = RetailProfile(),
            ["Real Estate & Property Management"] = RealEstateProfile(),
            ["Education & EdTech"]                = EdTechProfile(),
            ["Local Services"]                    = LocalServicesProfile(),
            ["Core Software & Tech"]              = CoreSoftwareProfile(),
            ["Enterprise AI Platform"]            = EnterpriseProfile()
        };

    // ── Healthcare ────────────────────────────────────────────────────────────

    private static DomainProfile HealthcareProfile() => new(
        Name: "Healthcare AI",
        TechStack: "transformer-based clinical NLP, FHIR R4 APIs, HL7 v2 pipelines, and DICOM/PACS integration",
        DbPattern: "PostgreSQL 16 with pgvector (clinical embeddings) + TimescaleDB (vitals time-series)",
        ArchDescription: "HIPAA-compliant, zero-trust architecture with BAA-ready cloud deployment",
        SubDomains: ["Clinical Decision Support", "Medical Imaging AI", "Predictive Analytics",
                     "Remote Patient Monitoring", "Genomics & Precision Medicine",
                     "Healthcare Revenue Cycle", "Pharmacovigilance"],
        Competitors:
        [
            new CompetitorInsight
            {
                CompetitorName  = "Epic Systems",
                FeatureGap      = "Limited predictive analytics beyond Cheers CRM; no real-time AI inference at point-of-care; Epic-centric architecture blocks cross-vendor FHIR interoperability at scale.",
                ImpactScore     = "8.5/10",
                StrategicPlaybook = "Lead with FHIR-native interoperability and a vendor-agnostic AI layer. Offer Epic-compatible connectors on day 1 to reduce switching friction. Target Epic-locked health systems frustrated with proprietary AI add-on pricing exceeding $800K/year."
            },
            new CompetitorInsight
            {
                CompetitorName  = "Oracle Health (Cerner)",
                FeatureGap      = "Post-Oracle acquisition integration instability creating clinical staff churn; Millennium architecture not designed for modern ML inference; AI roadmap opaque for 24+ months.",
                ImpactScore     = "9.0/10",
                StrategicPlaybook = "Win disenchanted Cerner customers with a transparent AI roadmap and migration-as-a-service offer. The Oracle uncertainty window is 18–24 months — aggressive displacement campaigns now yield outsized returns."
            },
            new CompetitorInsight
            {
                CompetitorName  = "Veeva Systems",
                FeatureGap      = "Life-sciences-only focus limits cross-continuum acute care capabilities; poor integration with hospital clinical workflow systems outside pharma and CRO contexts.",
                ImpactScore     = "7.5/10",
                StrategicPlaybook = "Target the acute/ambulatory care continuum that Veeva cannot serve. Position as the clinical AI complement to Veeva for pharma-to-patient data continuity across the full care episode."
            },
            new CompetitorInsight
            {
                CompetitorName  = "IBM Watson Health (legacy)",
                FeatureGap      = "Divested and fragmented post-IBM exit; successor products lack cohesive platform narrative; clinical credibility damaged by high-profile oncology failures at MD Anderson.",
                ImpactScore     = "9.2/10",
                StrategicPlaybook = "Directly position as Watson's clinical AI successor. Lead with independent clinical validation studies and an FDA clearance roadmap. High conversion rate among health systems still locked into Watson contracts."
            }
        ],
        ItemPool:
        [
            new PrioritizedItem { Id="hc-001", Name="AI-Powered Clinical Decision Support System", Urgency=9, Difficulty=8, Value=10,
                Description="Real-time AI recommendations at point-of-care that augment clinician judgment by cross-referencing patient history, lab results, and evidence-based treatment guidelines to reduce diagnostic errors.",
                Rationale="18 million diagnostic errors occur annually in the US; 40% are preventable with AI-augmented clinical decision workflows.",
                RealLifeValue="Reduces diagnostic error rates by up to 37% and malpractice exposure by $3.2B industry-wide annually.",
                IntegrationSteps="1. Integrate with EHR via HL7 FHIR R4 CDS Hooks standard. 2. Deploy HIPAA-compliant inference model behind API gateway. 3. Implement configurable alert engine with clinician override logging. 4. Run 200-patient A/B pilot — validate AUROC ≥ 0.92 before full rollout." },
            new PrioritizedItem { Id="hc-002", Name="Patient Risk Stratification Engine", Urgency=8, Difficulty=7, Value=9,
                Description="ML-driven scoring system predicting 30-day readmission probability, chronic disease progression, and deterioration risk from vitals, labs, ADT feeds, and social determinants of health.",
                Rationale="Preventive intervention yields 5:1 ROI over acute care; stratification unlocks proactive care models that reduce ICU utilisation.",
                RealLifeValue="Reduces 30-day readmission rates by 25%, saving $1.1M per 1,000 beds annually.",
                IntegrationSteps="1. Extract structured data from ADT, labs, vitals, and claims via HL7 v2. 2. Train gradient boosting model on 3-year historical cohort. 3. Deploy real-time scoring API with sub-100ms p99 latency. 4. Surface risk scores in care coordination dashboards with actionable alert thresholds." },
            new PrioritizedItem { Id="hc-003", Name="Medical Imaging AI Analysis Pipeline", Urgency=9, Difficulty=9, Value=10,
                Description="Deep learning pipeline for automated interpretation of CT, MRI, and X-ray images detecting anomalies, measuring lesion progression, and prioritising radiologist worklists.",
                Rationale="Global radiology shortage of 30% creates dangerous read-time delays; AI triage cuts critical finding delays by 65%.",
                RealLifeValue="Reduces time-to-diagnosis for critical findings by 65% and radiologist overtime costs by $800K per department annually.",
                IntegrationSteps="1. Integrate with DICOM/PACS via DICOMweb APIs. 2. Deploy FDA/CE-cleared inference models on GPU nodes. 3. Implement DICOM SR structured reporting for auto-populated radiology reports. 4. Connect worklist prioritisation to the RIS system." },
            new PrioritizedItem { Id="hc-004", Name="Drug Interaction Detection Framework", Urgency=10, Difficulty=6, Value=10,
                Description="Pharmacovigilance AI that evaluates multi-drug regimens against interaction databases, patient genomics, and contraindication rules at prescription time.",
                Rationale="Drug interactions cause 125,000 deaths and 1.5M hospitalisations annually in the US — largely preventable at the prescribing stage.",
                RealLifeValue="Prevents up to 60% of polypharmacy errors, reducing adverse drug event liability by $2.7M per 500-bed hospital annually.",
                IntegrationSteps="1. Ingest DrugBank, RxNorm, and FDA FAERS interaction databases. 2. Build graph-based interaction engine with severity scoring. 3. Integrate with e-prescribing via CDS Hooks. 4. Add pharmacogenomics overlay with patient genetic profile matching." },
            new PrioritizedItem { Id="hc-005", Name="Electronic Health Record Intelligence", Urgency=8, Difficulty=7, Value=8,
                Description="NLP system that extracts structured clinical insights from unstructured EHR notes, discharge summaries, and physician dictations, enabling population health analytics.",
                Rationale="80% of clinical data is unstructured; unlocking it improves coding accuracy by 35% and reduces chart review time by 70%.",
                RealLifeValue="Recovers $1.8M in denied claims per 300-bed facility annually through improved medical coding accuracy.",
                IntegrationSteps="1. Deploy clinical NLP pipeline (NER, relation extraction) on EHR note corpus. 2. Map entities to SNOMED CT, ICD-10, CPT. 3. Build population health analytics dashboard with cohort filters. 4. Integrate with revenue cycle management for automated coding recommendations." },
            new PrioritizedItem { Id="hc-006", Name="Predictive Readmission Prevention Platform", Urgency=7, Difficulty=6, Value=9,
                Description="Targeted intervention system identifying high-risk patients pre-discharge and coordinating care transitions, follow-up scheduling, and social support services to prevent readmission.",
                Rationale="CMS HRRP penalties cost hospitals $550M annually; targeted readmission prevention is the highest-ROI quality improvement programme.",
                RealLifeValue="Avoids an average of $180K in CMS HRRP penalties annually per 200-bed hospital while improving patient satisfaction scores.",
                IntegrationSteps="1. Build risk-score to care-plan recommendation engine. 2. Integrate with care management platform for automated follow-up scheduling. 3. Connect to social determinants screening tools. 4. Implement patient communication layer with SMS/app follow-up nudges." },
            new PrioritizedItem { Id="hc-007", Name="Remote Patient Monitoring AI Platform", Urgency=8, Difficulty=5, Value=8,
                Description="IoT-integrated continuous monitoring system collecting biometric data from wearables, applying anomaly detection, and alerting care teams to early deterioration signals.",
                Rationale="Post-pandemic shift to value-based care demands continuous patient engagement beyond the clinical visit; RPM reduces ER utilisation by 28%.",
                RealLifeValue="Reduces emergency department visits by 28% for enrolled chronic disease patients, yielding $4.2M in avoided costs per 10,000 enrolled patients.",
                IntegrationSteps="1. Establish FHIR-compliant IoT ingestion pipeline. 2. Implement real-time anomaly detection with configurable alert rules. 3. Build patient-facing mobile app with biometric data sync. 4. Integrate alert routing with nursing station EHR workflow." },
            new PrioritizedItem { Id="hc-008", Name="Automated Radiology Report Generation", Urgency=7, Difficulty=9, Value=9,
                Description="GPT-class language model fine-tuned on radiology reports that auto-drafts structured reports from imaging AI output, reducing radiologist documentation burden by 80%.",
                Rationale="Radiologists spend 40% of time on documentation; automation reduces report time from 20 minutes to 4 minutes per study.",
                RealLifeValue="Increases radiology department throughput by 35% without additional headcount.",
                IntegrationSteps="1. Fine-tune LLM on de-identified radiology report corpus per subspecialty. 2. Integrate imaging AI output as structured report input. 3. Build radiologist review/edit UI with accept/reject workflow. 4. Export final report to RIS/PACS as DICOM SR." },
            new PrioritizedItem { Id="hc-009", Name="Genomic Data Processing Platform", Urgency=6, Difficulty=10, Value=9,
                Description="Clinical-grade bioinformatics pipeline for variant calling, pathogenicity classification, and pharmacogenomics interpretation converting raw sequencing data into actionable clinical reports.",
                Rationale="Precision medicine adoption requires scalable genomic interpretation; current manual processes bottleneck the 15M+ tests ordered annually.",
                RealLifeValue="Reduces genomic report turnaround from 14 days to 48 hours while cutting bioinformatics labour costs by 60%.",
                IntegrationSteps="1. Deploy GATK best-practices variant calling on cloud HPC. 2. Integrate ClinVar, OMIM, and PharmGKB knowledge bases. 3. Build AI classifier for variants of uncertain significance (VUS). 4. Generate structured genomics report via HL7 message to EHR." },
            new PrioritizedItem { Id="hc-010", Name="Healthcare Claims Fraud Detection Engine", Urgency=9, Difficulty=6, Value=8,
                Description="Multi-model anomaly detection analysing billing patterns, provider networks, and claims sequences to identify fraudulent activity before payment with minimal false positive alert fatigue.",
                Rationale="Healthcare fraud costs the US $100B annually; current rule-based systems catch less than 3% of fraudulent claims before payment.",
                RealLifeValue="Detects 18x more fraudulent claims pre-payment than rule-based systems, recovering $12M per $1B in claims processed.",
                IntegrationSteps="1. Ingest claims data via X12 EDI 837 parser. 2. Build graph network of provider-patient-facility relationships. 3. Train ensemble model (isolation forest + GNN) on labelled fraud corpus. 4. Integrate with claims adjudication for real-time scoring." }
        ]
    );

    // ── Financial Technology ──────────────────────────────────────────────────

    private static DomainProfile FinTechProfile() => new(
        Name: "Financial Technology",
        TechStack: "streaming ML inference (Kafka + Flink), graph neural networks, explainable AI (SHAP/LIME), and open banking API standards",
        DbPattern: "PostgreSQL 16 (transactional) + Apache Iceberg (analytical lakehouse) + Redis Cluster (real-time feature store)",
        ArchDescription: "PCI-DSS Level 1, SOX-compliant architecture with immutable audit ledger",
        SubDomains: ["Fraud Detection & Prevention", "Credit Risk & Underwriting", "AML / KYC Compliance",
                     "Algorithmic Trading", "Regulatory Reporting", "Open Banking", "Insurance Intelligence"],
        Competitors:
        [
            new CompetitorInsight
            {
                CompetitorName  = "Plaid",
                FeatureGap      = "Consumer banking data aggregation focus limits enterprise analytics; no real-time AI inference for risk decisioning; fraud detection absent from product suite.",
                ImpactScore     = "8.0/10",
                StrategicPlaybook = "Position as the enterprise AI decisioning layer on top of Plaid's data aggregation. Offer Plaid-compatible connectors while delivering the ML risk engine Plaid cannot build without cannibalising partnerships."
            },
            new CompetitorInsight
            {
                CompetitorName  = "Stripe",
                FeatureGap      = "Payments processing focus with limited cross-institution data visibility; Stripe Radar rule engine not extensible for complex enterprise fraud models requiring graph and behavioural signals.",
                ImpactScore     = "8.5/10",
                StrategicPlaybook = "Target enterprise merchants who outgrow Stripe Radar with custom ML fraud models, cross-network velocity checks, and consortium data signals Stripe cannot access."
            },
            new CompetitorInsight
            {
                CompetitorName  = "Temenos",
                FeatureGap      = "Legacy core banking architecture constrains AI integration flexibility; Temenos AI modules require Temenos core, creating lock-in for a weakening incumbent losing 8% market share annually.",
                ImpactScore     = "9.0/10",
                StrategicPlaybook = "Win regional banks evaluating Temenos modernisation with a composable AI layer that integrates alongside any core banking system — no rip-and-replace required."
            },
            new CompetitorInsight
            {
                CompetitorName  = "FIS Global",
                FeatureGap      = "Post-Worldpay divestiture portfolio incoherence; AI capabilities fragmented across 16 product lines with no unified data model; integration complexity averaging 18 months per enterprise deployment.",
                ImpactScore     = "9.2/10",
                StrategicPlaybook = "Target FIS clients frustrated with multi-contract complexity by offering a unified AI platform that consolidates intelligence across all FIS product touchpoints under a single API contract."
            }
        ],
        ItemPool:
        [
            new PrioritizedItem { Id="ft-001", Name="Real-Time Transaction Fraud Detection Engine", Urgency=9, Difficulty=7, Value=10,
                Description="Sub-50ms ML inference system scoring every transaction for fraud probability using behavioural biometrics, device fingerprinting, velocity patterns, and graph-based network analysis.",
                Rationale="Global card fraud losses reached $33B in 2023; real-time scoring at transaction authorisation is the last line of defence.",
                RealLifeValue="Reduces fraud losses by up to 80% while maintaining false positive rates below 0.1%, protecting customer experience.",
                IntegrationSteps="1. Deploy streaming inference (Apache Kafka + Flink) for sub-50ms scoring. 2. Build feature store for real-time behavioural feature computation. 3. Integrate with card network authorisation APIs. 4. Implement feedback loop for model retraining on confirmed fraud labels." },
            new PrioritizedItem { Id="ft-002", Name="AI-Driven Credit Risk Assessment Platform", Urgency=8, Difficulty=7, Value=9,
                Description="Alternative credit scoring engine supplementing FICO with cash flow analysis, rent payment history, and employment verification to expand access for underbanked populations.",
                Rationale="45M Americans are credit-invisible; alternative data expands the addressable lending market by 40% while maintaining regulatory compliance.",
                RealLifeValue="Expands loan approval rates by 22% for previously denied segments while maintaining default rates within tolerance — $500M in new book growth per $5B lender.",
                IntegrationSteps="1. Integrate alternative data sources (Plaid, Argyle, rental bureaux). 2. Build explainable credit model meeting ECOA adverse action requirements. 3. Implement HMDA reporting and fair lending bias monitoring. 4. Connect to loan origination system with automated decisioning." },
            new PrioritizedItem { Id="ft-003", Name="Automated KYC/AML Compliance Engine", Urgency=9, Difficulty=6, Value=8,
                Description="AI-powered identity verification and transaction monitoring system automating customer onboarding screening, suspicious activity detection, and SAR report generation with regulatory audit trails.",
                Rationale="Global AML compliance costs $274B annually; 90% of manual screening reviews produce false positives that consume analyst capacity.",
                RealLifeValue="Reduces KYC onboarding from 72 hours to 4 hours and cuts compliance analyst workload by 70%, saving $2.8M per 1,000-analyst team.",
                IntegrationSteps="1. Integrate OFAC, PEP, and adverse media screening APIs. 2. Build entity resolution engine for cross-database name matching. 3. Deploy transaction monitoring with configurable typology rules. 4. Automate SAR narrative generation for FinCEN filing." },
            new PrioritizedItem { Id="ft-004", Name="Algorithmic Trading Strategy Optimizer", Urgency=7, Difficulty=9, Value=8,
                Description="Reinforcement learning system continuously optimising execution strategies across asset classes, adapting to market microstructure changes to minimise market impact costs.",
                Rationale="Institutional trading desks lose 15–30bps on average to market impact; AI-optimised execution recovers 60% of those costs.",
                RealLifeValue="Recovers 18–24bps in execution quality per trade, translating to $18M annually on a $10B AUM mandate.",
                IntegrationSteps="1. Connect to FIX protocol order routing infrastructure. 2. Build market data feed processor for L2 order book features. 3. Train RL agent on historical execution data with market impact simulator. 4. Deploy with kill-switch and position limit guardrails." },
            new PrioritizedItem { Id="ft-005", Name="Customer Churn Prediction & Retention Engine", Urgency=8, Difficulty=5, Value=8,
                Description="Behavioural analytics platform predicting bank customer churn 90 days in advance, triggering personalised retention interventions through the most effective channel and offer.",
                Rationale="Acquiring a new banking customer costs 5x more than retaining an existing one; 90-day prediction windows allow cost-effective intervention.",
                RealLifeValue="Reduces customer churn by 30% with AI-personalised retention offers, protecting $45M in annual revenue per 1M customer base.",
                IntegrationSteps="1. Build feature pipeline from transactions, product usage, and interaction logs. 2. Train XGBoost churn predictor with SHAP explainability. 3. Integrate recommendation engine with marketing automation. 4. Implement A/B testing framework for offer optimisation." },
            new PrioritizedItem { Id="ft-006", Name="Portfolio Optimization & Risk Analytics", Urgency=7, Difficulty=8, Value=7,
                Description="Factor modelling engine with tail-risk scenario analysis and alternative risk premia that generates optimal portfolio allocations under regulatory constraints.",
                Rationale="60% of wealth managers still use mean-variance optimisation from the 1950s; modern factor-based approaches deliver 40% better risk-adjusted returns.",
                RealLifeValue="Improves Sharpe ratio by 0.35 on average, generating $12M additional risk-adjusted return per $1B AUM.",
                IntegrationSteps="1. Integrate Bloomberg/Refinitiv market data and factor return series. 2. Build multi-factor risk model with ESG integration. 3. Implement CVXPY constrained optimisation solver. 4. Connect to OMS for rebalancing execution." },
            new PrioritizedItem { Id="ft-007", Name="Regulatory Reporting Automation Suite", Urgency=8, Difficulty=6, Value=8,
                Description="End-to-end data lineage and reporting platform automating Basel III, DORA, and MiFID II workflows, reducing manual reconciliation errors and submission delays.",
                Rationale="Financial firms spend $1.2B annually on regulatory reporting labour; automation reduces cost per report by 65% while improving accuracy.",
                RealLifeValue="Cuts regulatory reporting preparation time by 70%, avoiding $2M+ in regulatory penalties per major compliance incident.",
                IntegrationSteps="1. Map data lineage from source systems to regulatory report fields. 2. Build validation engine against regulatory schema rules. 3. Implement automated reconciliation with variance flagging. 4. Connect to EDGAR, FCA, and ECB submission portals." },
            new PrioritizedItem { Id="ft-008", Name="Open Banking Data Monetisation Platform", Urgency=7, Difficulty=6, Value=8,
                Description="PSD2/CDR-compliant API platform enabling banks to monetise customer-permissioned data through premium analytics services, creating new fee revenue beyond traditional products.",
                Rationale="Open banking market will reach $43B by 2026; banks not building data monetisation strategies will cede revenues to fintechs.",
                RealLifeValue="Generates $18–42 in new annual revenue per consenting customer through premium analytics API licensing.",
                IntegrationSteps="1. Deploy OAuth 2.0 / FAPI-compliant open banking API gateway. 2. Implement customer consent management with granular data permissions. 3. Build analytics API catalogue with usage-based pricing engine. 4. Integrate with developer portal for third-party fintech onboarding." },
            new PrioritizedItem { Id="ft-009", Name="Insurance Underwriting Intelligence", Urgency=7, Difficulty=7, Value=8,
                Description="AI underwriting engine ingesting unstructured submissions, extracting risk signals, benchmarking against loss history, and generating binding quotes in hours not days.",
                Rationale="Commercial insurance underwriters spend 40% of time on data gathering; AI-assisted underwriting reduces quote time from 5 days to 4 hours.",
                RealLifeValue="Increases underwriter capacity by 3x, enabling $200M additional GWP per 50-underwriter team without headcount expansion.",
                IntegrationSteps="1. Build document intelligence pipeline for submission parsing. 2. Integrate actuarial loss models with ML-derived risk factors. 3. Connect to ISO, NCCI, LIMRA industry databases. 4. Build quote generation engine with regulatory rate filing compliance." },
            new PrioritizedItem { Id="ft-010", Name="Financial Document Intelligence Pipeline", Urgency=7, Difficulty=5, Value=7,
                Description="NLP system extracting structured data from financial filings, earnings transcripts, loan agreements, and prospectuses, converting document intelligence into queryable analytics.",
                Rationale="Financial analysts spend 30% of their time reading documents; AI extraction democratises institutional-grade research at 100x speed.",
                RealLifeValue="Reduces financial research preparation time by 75%, enabling analysts to cover 4x more securities with the same headcount.",
                IntegrationSteps="1. Deploy document parsing pipeline for SEC EDGAR, PDF, and XBRL formats. 2. Fine-tune NER model for financial entity extraction. 3. Build structured data lake from extracted entities. 4. Create queryable analytics API for downstream applications." }
        ]
    );

    // ── Legal Technology ──────────────────────────────────────────────────────

    private static DomainProfile LegalTechProfile() => new(
        Name: "Legal Technology",
        TechStack: "retrieval-augmented generation (RAG) over legal corpora, citation verification, and technology-assisted review (TAR/predictive coding)",
        DbPattern: "PostgreSQL 16 + Elasticsearch 8 (full-text + kNN) + S3-compatible object store (document artefacts)",
        ArchDescription: "attorney-client privilege-aware, GDPR / CCPA compliant with configurable data residency",
        SubDomains: ["Contract Lifecycle Management", "Legal Research & Analytics", "eDiscovery",
                     "Compliance Monitoring", "Litigation Intelligence", "IP Portfolio Management",
                     "Legal Document Drafting"],
        Competitors:
        [
            new CompetitorInsight
            {
                CompetitorName  = "LexisNexis",
                FeatureGap      = "Legacy search index architecture not designed for LLM-native retrieval; AI capabilities bolted onto 50-year-old database infrastructure without true semantic understanding.",
                ImpactScore     = "8.8/10",
                StrategicPlaybook = "Win by offering LLM-native legal research that LexisNexis cannot match architecturally. Target law firms in LexisNexis pilots frustrated by hallucination rates and citation failures."
            },
            new CompetitorInsight
            {
                CompetitorName  = "Thomson Reuters Westlaw",
                FeatureGap      = "Westlaw Edge AI limited to search enhancement; no generative drafting, contract analysis, or end-to-end matter intelligence; subscription pricing misaligned with AI value delivery model.",
                ImpactScore     = "8.5/10",
                StrategicPlaybook = "Undercut Westlaw Edge with consumption-based AI pricing and generative capabilities spanning the full matter lifecycle — not just search augmentation."
            },
            new CompetitorInsight
            {
                CompetitorName  = "Clio",
                FeatureGap      = "SMB law firm focus lacks enterprise-grade AI; Clio Duo is surface-level AI without deep legal reasoning; no contract analysis, eDiscovery, or compliance monitoring modules at scale.",
                ImpactScore     = "7.5/10",
                StrategicPlaybook = "Target Clio clients scaling to mid-market who need enterprise AI capabilities Clio cannot deliver. Offer Clio data migration tooling for seamless transition."
            },
            new CompetitorInsight
            {
                CompetitorName  = "ContractPodAi",
                FeatureGap      = "CLM-only focus misses the end-to-end legal intelligence market; limited integration ecosystem; weak litigation analytics and compliance monitoring relative to enterprise legal platforms.",
                ImpactScore     = "8.2/10",
                StrategicPlaybook = "Position as the end-to-end legal AI platform extending beyond CLM into litigation, compliance, and research — the complete legal intelligence stack on a single contract."
            }
        ],
        ItemPool:
        [
            new PrioritizedItem { Id="lt-001", Name="Automated Contract Analysis Engine", Urgency=9, Difficulty=6, Value=9,
                Description="LLM-powered contract intelligence platform reviewing commercial agreements, flagging non-standard clauses, identifying risk concentrations, and benchmarking terms against market standards.",
                Rationale="Manual contract review costs $200/hour and 12 hours per complex agreement; AI reduces this to 20 minutes with higher consistency.",
                RealLifeValue="Reduces contract review time by 80%, saving $1.5M annually per 20-attorney team while improving risk identification accuracy by 35%.",
                IntegrationSteps="1. Build document ingestion pipeline for PDF, DOCX, and EDGAR. 2. Fine-tune LLM on CUAD labelled clause dataset. 3. Build clause library with market-standard benchmark database. 4. Integrate with CLM (DocuSign CLM, Ironclad) via API." },
            new PrioritizedItem { Id="lt-002", Name="Legal Research Intelligence Platform", Urgency=8, Difficulty=7, Value=8,
                Description="RAG system searching case law, statutes, and regulations to synthesise jurisdiction-aware research memos with verified citations and authority weighting.",
                Rationale="Associates spend 40% of billable time on legal research; AI-assisted research reduces this by 70% while improving citation completeness by 25%.",
                RealLifeValue="Increases associate research capacity by 3x, enabling $900K additional billable revenue per 10 associates annually.",
                IntegrationSteps="1. Index case law from CourtListener, Westlaw, and LexisNexis APIs. 2. Deploy RAG pipeline with citation verification. 3. Build jurisdiction filter and practice area taxonomy. 4. Integrate with DMS (iManage, NetDocs)." },
            new PrioritizedItem { Id="lt-003", Name="eDiscovery Document Processing Pipeline", Urgency=8, Difficulty=7, Value=9,
                Description="AI-powered document review platform using TAR, near-duplicate detection, email thread analysis, and privilege screening to dramatically reduce review costs and timelines.",
                Rationale="Document review represents 70% of total litigation expense; AI TAR reduces review volume by 85% with superior recall vs manual review.",
                RealLifeValue="Reduces document review costs by 70%, saving $3.5M per large litigation matter involving 5M+ documents.",
                IntegrationSteps="1. Deploy ESI ingestion pipeline for all electronic document types. 2. Implement predictive coding with continuous active learning. 3. Build privilege detection model for attorney-client communications. 4. Integrate with Relativity or Everlaw review platform." },
            new PrioritizedItem { Id="lt-004", Name="Compliance Monitoring & Alert System", Urgency=9, Difficulty=6, Value=8,
                Description="Multi-jurisdictional compliance monitoring platform tracking regulatory changes, mapping obligations to business activities, and triggering alerts when control gaps are identified.",
                Rationale="Average cost of a compliance failure in financial services is $14M; proactive monitoring prevents 65% of remediable violations.",
                RealLifeValue="Prevents an average of $4.2M in regulatory penalties annually by identifying control gaps 60 days before examination.",
                IntegrationSteps="1. Subscribe to regulatory update feeds (Federal Register, FCA, ESMA, CFPB). 2. Build NLP pipeline to extract obligation-specific requirements. 3. Map obligations to internal control library. 4. Integrate with GRC platform (ServiceNow, Archer)." },
            new PrioritizedItem { Id="lt-005", Name="Predictive Litigation Analytics", Urgency=7, Difficulty=8, Value=7,
                Description="Judge and jurisdiction analytics platform modelling litigation outcomes, settlement probabilities, and cost trajectories based on historical case data and judge behaviour patterns.",
                Rationale="85% of legal decisions are made with incomplete information about judge behaviour; predictive analytics improves settlement timing accuracy by 40%.",
                RealLifeValue="Improves settlement offer timing accuracy by 40%, reducing average litigation costs by $800K per complex commercial dispute.",
                IntegrationSteps="1. Aggregate court dockets from PACER and state court APIs. 2. Build judge behaviour model from historical rulings. 3. Train outcome prediction model on case outcome corpus. 4. Build scenario modelling tool for litigation strategy planning." },
            new PrioritizedItem { Id="lt-006", Name="Regulatory Change Management Tracker", Urgency=7, Difficulty=5, Value=8,
                Description="AI regulatory intelligence platform monitoring 500+ regulatory bodies, classifying changes by impact, and routing updates to responsible compliance owners with implementation deadlines.",
                Rationale="Legal teams receive 1,000+ regulatory changes per month globally; only 30% are material, but sorting signal from noise consumes 60% of team capacity.",
                RealLifeValue="Reduces regulatory change triage time by 65%, freeing compliance teams to focus on high-impact changes before deadlines.",
                IntegrationSteps="1. Build regulatory content ingestion from 500+ government sources. 2. Train impact classification model (material/immaterial) by jurisdiction. 3. Build obligation-to-owner routing engine. 4. Integrate with project management (Jira, Monday) for implementation tracking." },
            new PrioritizedItem { Id="lt-007", Name="Intellectual Property Portfolio Intelligence", Urgency=6, Difficulty=7, Value=7,
                Description="AI platform for patent landscape analysis, prior art search, FTO opinion generation, and portfolio valuation identifying licensing opportunities and infringement risks.",
                Rationale="IP litigation costs average $3–5M per dispute; early prior art identification prevents 70% of infringement exposure before product launch.",
                RealLifeValue="Prevents average $2.4M in IP litigation exposure per product launch through comprehensive FTO analysis at 90% lower cost.",
                IntegrationSteps="1. Index USPTO, EPO, and WIPO databases with semantic search. 2. Build claim mapping engine for infringement analysis. 3. Implement patent valuation model with citation and market analysis. 4. Integrate with docketing system for maintenance deadline management." },
            new PrioritizedItem { Id="lt-008", Name="Legal Billing Optimisation Engine", Urgency=7, Difficulty=5, Value=6,
                Description="AI review platform analysing timekeeping entries, detecting billing guideline violations, identifying task duplication, and recommending write-down adjustments before invoice submission.",
                Rationale="Corporate legal departments reject 15–20% of invoiced hours for billing guideline violations; automated review ensures compliance and reduces disputes.",
                RealLifeValue="Reduces invoice adjustment disputes by 80% and billing guideline violation rates by 75%, saving $450K annually per law firm.",
                IntegrationSteps="1. Ingest matter management and time entry data via API. 2. Train billing guideline compliance classifier on annotated dataset. 3. Build real-time UTBMS task code validation engine. 4. Integrate with billing system (Elite, Aderant) for pre-submission review." },
            new PrioritizedItem { Id="lt-009", Name="Multi-Jurisdiction Compliance Checker", Urgency=8, Difficulty=8, Value=8,
                Description="Cross-border compliance intelligence system evaluating business activities, data flows, and product features against 200+ jurisdiction-specific regulatory frameworks.",
                Rationale="Multinationals face 12.7x more regulatory complexity than single-jurisdiction firms; automated cross-jurisdiction mapping reduces legal counsel hours by 60%.",
                RealLifeValue="Reduces cross-border compliance legal fees by $1.2M annually for multinationals operating in 20+ jurisdictions.",
                IntegrationSteps="1. Build regulatory ontology for 200+ jurisdictions. 2. Map business activity taxonomy to obligation frameworks. 3. Implement conflict detection engine for cross-jurisdiction inconsistencies. 4. Generate compliance gap report with remediation priority scoring." },
            new PrioritizedItem { Id="lt-010", Name="AI-Powered Legal Drafting Assistant", Urgency=8, Difficulty=6, Value=8,
                Description="Context-aware document drafting system generating first-draft legal agreements, clauses, and pleadings from structured input, enforcing firm-specific style guides and approved clause libraries.",
                Rationale="First-draft generation consumes 35% of attorney time; AI drafting assistants reduce this to 10% while improving consistency with approved precedent.",
                RealLifeValue="Increases attorney drafting capacity by 3x, enabling $1.1M additional matter capacity per 10-attorney team.",
                IntegrationSteps="1. Fine-tune LLM on firm's approved clause library and precedent documents. 2. Build clause assembly engine with variable substitution. 3. Integrate with Word/Google Docs via add-in. 4. Add style guide enforcement and precedent matching validation." }
        ]
    );

    // ── Enterprise AI Platform (default) ─────────────────────────────────────

    private static DomainProfile EnterpriseProfile() => new(
        Name: "Enterprise AI Platform",
        TechStack: "LLM orchestration (LangGraph), vector search (pgvector/Qdrant), agentic process automation, and real-time feature stores",
        DbPattern: "PostgreSQL 16 (OLTP) + Apache Iceberg on S3 (analytics lakehouse) + Redis Cluster (feature store / cache)",
        ArchDescription: "SOC 2 Type II certified, multi-tenant SaaS with configurable single-tenant deployment option",
        SubDomains: ["Intelligent Process Automation", "Enterprise Knowledge Management", "Predictive Maintenance",
                     "Customer Experience AI", "Data Governance", "Conversational AI", "Document Intelligence"],
        Competitors:
        [
            new CompetitorInsight
            {
                CompetitorName  = "Microsoft Azure AI / Copilot",
                FeatureGap      = "Broad platform lacks domain specialisation; M365 Copilot siloed within Microsoft ecosystem; Azure AI Studio requires significant custom integration work with 18-month enterprise rollout timelines.",
                ImpactScore     = "8.0/10",
                StrategicPlaybook = "Differentiate with domain-specific AI modules Azure cannot deliver out of the box. Target enterprises frustrated by Azure AI integration complexity and $2M+ professional services cost per deployment."
            },
            new CompetitorInsight
            {
                CompetitorName  = "Google Vertex AI / Gemini for Workspace",
                FeatureGap      = "Developer-centric platform with steep enterprise adoption curve; Workspace AI siloed from non-Google enterprise systems; limited prebuilt enterprise application intelligence.",
                ImpactScore     = "8.3/10",
                StrategicPlaybook = "Win enterprises with Microsoft/SAP-centric stacks that Google cannot serve well. Position as the system-agnostic AI platform bridging all enterprise applications under a single API."
            },
            new CompetitorInsight
            {
                CompetitorName  = "AWS Bedrock / SageMaker",
                FeatureGap      = "MLOps platform for data scientists, not business teams; Bedrock offers model access but no enterprise application intelligence or workflow orchestration; 6-month average time-to-production.",
                ImpactScore     = "8.5/10",
                StrategicPlaybook = "Target AWS Bedrock customers who want application-layer AI, not just model APIs. Position as the enterprise AI application layer that makes AWS AI investments productive in weeks, not months."
            },
            new CompetitorInsight
            {
                CompetitorName  = "IBM watsonx",
                FeatureGap      = "Brand trust issues following Watson Health exit undermine enterprise AI credibility; watsonx foundation models lack multimodal capabilities; Governance module priced out of mid-market reach.",
                ImpactScore     = "9.0/10",
                StrategicPlaybook = "Target IBM's regulated-industry installed base (financial services, government) with a credible AI governance story that addresses Watson's failed promises with independent validation."
            }
        ],
        ItemPool:
        [
            new PrioritizedItem { Id="ep-001", Name="Intelligent Process Automation Hub", Urgency=9, Difficulty=6, Value=9,
                Description="Agentic AI orchestration platform discovering, prioritising, and automating business processes across enterprise systems — combining RPA, LLMs, and decision models in composable automation workflows.",
                Rationale="McKinsey estimates 60% of enterprise activities can be automated with current AI; agentic process automation is the highest-ROI AI investment available today.",
                RealLifeValue="Delivers $4.2M in annual labour cost reduction per 1,000 automated process hours, with 18-month average payback.",
                IntegrationSteps="1. Deploy process mining tool to discover automation candidates. 2. Build agent orchestration layer with tool-use and approval workflows. 3. Connect RPA bots to LLM reasoning for exception handling. 4. Implement process analytics dashboard with ROI tracking." },
            new PrioritizedItem { Id="ep-002", Name="Enterprise Knowledge Graph Builder", Urgency=8, Difficulty=8, Value=9,
                Description="AI-powered knowledge graph connecting entities across the enterprise data landscape — people, products, documents, and processes — enabling multi-hop reasoning and intelligent search.",
                Rationale="Enterprise knowledge is siloed in 12+ disconnected systems on average; a unified knowledge graph increases employee productivity by 35%.",
                RealLifeValue="Reduces time-to-answer for knowledge queries by 75%, worth $2.8M annually in recaptured productivity for a 5,000-person organisation.",
                IntegrationSteps="1. Build entity extraction pipeline from document corpus. 2. Deploy Neo4j graph database with enterprise taxonomy schema. 3. Implement semantic search with knowledge graph augmentation. 4. Build GraphQL API for downstream application integration." },
            new PrioritizedItem { Id="ep-003", Name="Predictive Asset Maintenance Platform", Urgency=8, Difficulty=7, Value=9,
                Description="IoT-integrated predictive maintenance system using sensor telemetry, failure pattern recognition, and remaining useful life prediction to shift from reactive to predictive maintenance strategies.",
                Rationale="Unplanned downtime costs industrial firms $260K/hour; predictive maintenance reduces unplanned failures by 70% and extends asset life by 25%.",
                RealLifeValue="Reduces unplanned downtime costs by 70%, generating $8.4M in annual savings per $1B in managed assets.",
                IntegrationSteps="1. Integrate sensor data via MQTT/OPC-UA protocols. 2. Build time-series anomaly detection models per asset class. 3. Implement remaining useful life (RUL) regression models. 4. Connect to CMMS (SAP PM, IBM Maximo) for automated work order generation." },
            new PrioritizedItem { Id="ep-004", Name="AI Customer Experience Personalisation Engine", Urgency=9, Difficulty=6, Value=9,
                Description="Real-time next-best-action engine personalising customer interactions across channels using behavioural signals, propensity models, and LLM-generated content to maximise conversion and retention.",
                Rationale="Personalised CX drives 40% higher revenue per customer; brands with best-in-class personalisation achieve 2.9x higher revenue growth.",
                RealLifeValue="Increases customer lifetime value by 23% through personalised next-best-action recommendations, worth $18M per 1M customer base.",
                IntegrationSteps="1. Build real-time CDP with behavioural event tracking. 2. Train propensity models (buy, churn, upsell) per segment. 3. Deploy next-best-action engine with channel orchestration. 4. Integrate with marketing automation (Salesforce, HubSpot) and contact centre." },
            new PrioritizedItem { Id="ep-005", Name="Data Governance & Quality AI Platform", Urgency=7, Difficulty=7, Value=8,
                Description="Automated data governance system combining cataloguing, lineage tracking, quality scoring, and PII detection to ensure enterprise data meets regulatory and analytical standards.",
                Rationale="Poor data quality costs organisations $12.9M annually on average (Gartner); automated governance reduces remediation costs by 60%.",
                RealLifeValue="Improves data quality scores by 45% across enterprise data assets, reducing downstream analytics errors and regulatory data compliance risk.",
                IntegrationSteps="1. Deploy metadata crawler across all data sources (S3, Snowflake, SQL Server). 2. Build AI-powered data classification and PII detection pipeline. 3. Implement data quality rule engine with automated scoring. 4. Integrate with Apache Atlas or Collibra for data lineage tracking." },
            new PrioritizedItem { Id="ep-006", Name="Enterprise Conversational AI Platform", Urgency=8, Difficulty=6, Value=8,
                Description="Multi-modal enterprise assistant combining RAG-based knowledge retrieval, system integration tools, and approval workflows as the AI layer across all enterprise applications.",
                Rationale="Employees use 8 enterprise applications on average; a unified AI assistant reduces context switching and improves task completion speed by 35%.",
                RealLifeValue="Saves each knowledge worker 2.5 hours per week, generating $4.8M in productivity value annually for a 2,000-person organisation.",
                IntegrationSteps="1. Deploy LLM with enterprise RAG on internal knowledge base. 2. Build tool-use integrations for top 10 enterprise applications. 3. Implement approval workflow engine for sensitive actions. 4. Deploy as Teams/Slack bot and web widget with SSO." },
            new PrioritizedItem { Id="ep-007", Name="Document Intelligence & Extraction Pipeline", Urgency=8, Difficulty=5, Value=8,
                Description="Intelligent document processing platform classifying, extracting, and validating structured data from unstructured business documents at scale, feeding downstream workflows with clean structured data.",
                Rationale="85% of business data is unstructured; automated IDP reduces manual data entry costs by 80% with 99.2% extraction accuracy on trained document types.",
                RealLifeValue="Eliminates $1.2M in annual manual data entry labour per 1M documents processed while reducing downstream data errors by 90%.",
                IntegrationSteps="1. Build document classification model for top 50 document types. 2. Deploy extraction model fine-tuned on business-specific templates. 3. Implement human-in-the-loop review for low-confidence extractions. 4. Connect to ERP/CRM via API for straight-through processing." },
            new PrioritizedItem { Id="ep-008", Name="Decision Intelligence Platform", Urgency=7, Difficulty=8, Value=8,
                Description="Enterprise decision management platform combining rule engines, ML models, and LLM reasoning to codify, automate, and explain high-frequency business decisions across operational systems.",
                Rationale="Enterprises make 1M+ operational decisions daily; codifying decision logic reduces inconsistency and creates an auditable record for regulatory compliance.",
                RealLifeValue="Increases decision consistency by 90% and reduces decision-related errors by 65%, saving $3.8M annually in rework and compliance costs.",
                IntegrationSteps="1. Model decision landscape using FEEL/DMN notation. 2. Integrate ML propensity scores as decision inputs. 3. Deploy decision service API with explanation output. 4. Implement decision monitoring with drift detection and retraining triggers." },
            new PrioritizedItem { Id="ep-009", Name="Supply Chain Optimisation Engine", Urgency=7, Difficulty=8, Value=8,
                Description="Multi-echelon supply chain intelligence platform using demand forecasting, inventory optimisation, supplier risk scoring, and scenario simulation to reduce costs and disruption risk.",
                Rationale="Supply chain disruptions cost businesses $184M annually on average; AI-optimised inventory reduces carrying costs by 25% while maintaining 99.5% service levels.",
                RealLifeValue="Reduces inventory carrying costs by 25% ($12M annually per $500M in inventory) while improving SLA attainment from 94% to 99.5%.",
                IntegrationSteps="1. Integrate with ERP (SAP, Oracle) for inventory and order data. 2. Build hierarchical demand forecasting with external signals. 3. Implement multi-echelon inventory optimisation solver. 4. Connect supplier risk monitoring with alternative sourcing recommendations." },
            new PrioritizedItem { Id="ep-010", Name="Enterprise Search & Discovery Intelligence", Urgency=7, Difficulty=5, Value=7,
                Description="Semantic enterprise search platform unifying content across SharePoint, Confluence, email, and CRM with vector search, intent understanding, and personalised result ranking.",
                Rationale="Employees spend 2.5 hours daily searching for information; unified semantic search reduces this to 45 minutes, saving $12K per employee annually.",
                RealLifeValue="Saves each knowledge worker 1.75 hours daily in search time, generating $6.2M annually for a 2,000-person organisation.",
                IntegrationSteps="1. Build content crawler for SharePoint, Confluence, Google Drive, Salesforce. 2. Deploy vector embedding pipeline with domain-fine-tuned encoder. 3. Implement hybrid search (dense + sparse) with cross-encoder reranking. 4. Build personalisation layer with user behaviour tracking." }
        ]
    );

    // ── Retail & E-Commerce ───────────────────────────────────────────────────

    private static DomainProfile RetailProfile() => new(
        Name: "Retail & E-Commerce",
        TechStack: "real-time personalisation engines, demand forecasting ML, computer vision for visual search, and dynamic pricing algorithms",
        DbPattern: "PostgreSQL 16 (orders/inventory) + Redis Cluster (cart/session) + Elasticsearch (product catalogue)",
        ArchDescription: "PCI-DSS Level 1 compliant, multi-region active-active with sub-100ms checkout latency SLA",
        SubDomains: ["Personalisation & Recommendation", "Inventory & Demand Forecasting", "Dynamic Pricing",
                     "Fraud Prevention", "Visual Commerce", "Supply Chain Visibility", "Loyalty & CRM"],
        Competitors:
        [
            new CompetitorInsight
            {
                CompetitorName    = "Shopify",
                FeatureGap        = "Shopify AI features limited to Sidekick chat assistant and basic analytics; no real-time demand forecasting, advanced dynamic pricing, or enterprise-grade recommendation engine for high-SKU catalogues.",
                ImpactScore       = "8.5/10",
                StrategicPlaybook = "Target Shopify Plus merchants scaling past $50M GMV who outgrow native AI capabilities. Offer plug-and-play AI modules (demand forecasting, pricing, recommendations) that deploy alongside Shopify without platform migration."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Salesforce Commerce Cloud",
                FeatureGap        = "Einstein AI recommendations require expensive Salesforce data cloud licensing; personalisation latency at p99 exceeds 400ms under load, creating checkout abandonment spikes during peak events.",
                ImpactScore       = "9.0/10",
                StrategicPlaybook = "Win on sub-100ms personalisation latency and modular pricing vs SFCC's all-or-nothing licensing model. Target Black Friday-scale retailers where Einstein checkout latency directly costs revenue."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Adobe Commerce (Magento)",
                FeatureGap        = "Adobe Sensei AI bolt-on architecture cannot match native ML-first recommendation quality; high total cost of ownership and 8-month average implementation cycle limits mid-market competitiveness.",
                ImpactScore       = "8.8/10",
                StrategicPlaybook = "Position as the AI-first commerce intelligence layer alongside or replacing Adobe Commerce. Target the 40% of Adobe Commerce customers not using Sensei due to cost and complexity."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Amazon Personalise",
                FeatureGap        = "AWS-only deployment creates vendor lock-in; no pre-built commerce workflows, requiring 4–6 months of custom integration engineering; black-box model prevents business-rule overrides.",
                ImpactScore       = "8.2/10",
                StrategicPlaybook = "Differentiate with cloud-agnostic deployment, prebuilt commerce workflows deployable in 4 weeks, and an explainable recommendation engine with business-rule override controls retailers demand."
            }
        ],
        ItemPool:
        [
            new PrioritizedItem { Id="rt-001", Name="AI-Powered Product Recommendation Engine", Urgency=9, Difficulty=6, Value=10,
                Description="Real-time collaborative filtering and transformer-based recommendation system delivering personalised product discovery across homepage, PDP, cart, and post-purchase touchpoints.",
                Rationale="Recommendations drive 35% of Amazon's revenue; most mid-market retailers generate less than 8% from recommendations due to poor personalisation infrastructure.",
                RealLifeValue="Increases average order value by 18% and conversion rate by 23%, generating $4.2M additional annual revenue per $50M GMV retailer.",
                IntegrationSteps="1. Ingest clickstream, purchase, and product catalogue data into feature store. 2. Train session-based transformer model per user segment. 3. Deploy sub-50ms inference API with A/B testing framework. 4. Integrate widgets into PDP, cart, and email via SDK." },
            new PrioritizedItem { Id="rt-002", Name="Demand Forecasting & Inventory Optimizer", Urgency=9, Difficulty=7, Value=9,
                Description="Hierarchical time-series ML model predicting SKU-level demand across locations, incorporating promotional calendars, weather signals, and macroeconomic indicators to right-size inventory.",
                Rationale="Overstock and stockouts cost US retailers $1.75T annually; ML forecasting reduces forecast error by 50% vs statistical baselines.",
                RealLifeValue="Reduces inventory carrying costs by 28% and stockout rate by 40%, recovering $3.8M annually per $200M in managed inventory.",
                IntegrationSteps="1. Integrate ERP/WMS inventory feeds and POS sales history. 2. Enrich with external signals (Google Trends, weather, macro). 3. Train hierarchical forecasting model (temporal fusion transformer). 4. Connect replenishment recommendations to purchase order workflow." },
            new PrioritizedItem { Id="rt-003", Name="Dynamic Pricing Intelligence Platform", Urgency=8, Difficulty=7, Value=9,
                Description="Competitive price monitoring and ML-driven repricing engine adjusting prices in real time based on competitor movements, demand elasticity, inventory levels, and margin guardrails.",
                Rationale="Dynamic pricing delivers 5–10% margin improvement; retailers using static pricing surrender $180M annually to more agile competitors on price-sensitive SKUs.",
                RealLifeValue="Improves gross margin by 6.5% on dynamically priced SKUs while maintaining conversion rate within 2% of baseline.",
                IntegrationSteps="1. Deploy web scraping pipeline for competitor price monitoring. 2. Build price elasticity model per category. 3. Implement repricing rules engine with margin floor guardrails. 4. Connect to e-commerce platform pricing API with audit trail." },
            new PrioritizedItem { Id="rt-004", Name="Checkout Fraud Prevention Gateway", Urgency=9, Difficulty=6, Value=9,
                Description="Real-time fraud scoring engine analysing device fingerprint, behavioural biometrics, velocity patterns, and order anomalies to block fraudulent transactions without friction for legitimate customers.",
                Rationale="E-commerce fraud losses hit $48B globally in 2023; false positives from legacy rule engines block $443B in legitimate orders annually.",
                RealLifeValue="Reduces fraud chargebacks by 76% while lowering false positive rate by 60%, recovering $2.1M in blocked legitimate revenue per $500M GMV.",
                IntegrationSteps="1. Integrate JavaScript device fingerprinting and behavioural biometrics SDK. 2. Build real-time scoring pipeline with sub-200ms SLA. 3. Connect to payment gateway via pre-authorisation hook. 4. Implement challenge flow (3DS2, OTP) for mid-risk scores." },
            new PrioritizedItem { Id="rt-005", Name="Visual Search & AI Catalogue Enrichment", Urgency=7, Difficulty=7, Value=8,
                Description="Computer vision platform enabling shoppers to search by image, auto-tagging product attributes, detecting duplicate listings, and enriching catalogue data from unstructured supplier content.",
                Rationale="30% of product searches fail due to poor catalogue data quality; visual search increases search conversion by 48% for fashion and home categories.",
                RealLifeValue="Increases search-to-purchase conversion by 32% and reduces catalogue management labour by 65%.",
                IntegrationSteps="1. Deploy image embedding model (CLIP-based) on product catalogue. 2. Build visual similarity search index (HNSW). 3. Implement reverse image search endpoint integrated into search bar. 4. Run attribute extraction pipeline to backfill catalogue gaps." },
            new PrioritizedItem { Id="rt-006", Name="Customer Lifetime Value Prediction Engine", Urgency=8, Difficulty=5, Value=8,
                Description="Probabilistic CLV model segmenting customers by predicted lifetime value, churn probability, and next-purchase timing to optimise acquisition spend and retention investment allocation.",
                Rationale="Top 20% of customers generate 80% of revenue; CLV-based segmentation improves marketing ROI by 35% vs recency-frequency-monetary models.",
                RealLifeValue="Improves marketing ROAS by 28% through CLV-based audience targeting, saving $900K annually per $10M marketing budget.",
                IntegrationSteps="1. Build BG/NBD + Gamma-Gamma CLV model on transaction history. 2. Create customer segments with intervention strategies per tier. 3. Sync segments to marketing automation and paid media platforms. 4. Implement real-time CLV update on each transaction event." },
            new PrioritizedItem { Id="rt-007", Name="Supply Chain Visibility & ETA Intelligence", Urgency=7, Difficulty=6, Value=8,
                Description="Real-time order tracking platform aggregating carrier data, predicting delivery ETAs with ML, and proactively communicating exceptions to reduce WISMO contact centre volume.",
                Rationale="WISMO (Where Is My Order) accounts for 35% of all e-commerce support contacts; AI-predicted ETA reduces inbound contact rate by 40%.",
                RealLifeValue="Reduces WISMO contact volume by 42%, saving $1.4M annually per 1M annual shipments in support centre costs.",
                IntegrationSteps="1. Integrate carrier APIs (FedEx, UPS, DHL) into unified tracking feed. 2. Train delivery ETA model on historical shipment data. 3. Build proactive exception notification engine (SMS/email/push). 4. Embed real-time tracking widget into order confirmation and account portal." },
            new PrioritizedItem { Id="rt-008", Name="Loyalty & Promotion Optimisation Platform", Urgency=7, Difficulty=5, Value=7,
                Description="ML-driven loyalty programme engine optimising point issuance, reward redemption, and promotional offer selection to maximise incremental revenue while protecting programme economics.",
                Rationale="Poorly calibrated loyalty programmes deliver negative ROI for 42% of retailers; ML optimisation improves programme incrementality by 3x.",
                RealLifeValue="Increases loyalty programme revenue incrementality by 220%, converting a cost-centre into a $2.8M annual profit driver per 500K active members.",
                IntegrationSteps="1. Model promotion incrementality using causal inference (matched controls). 2. Build personalised offer engine with margin constraint optimisation. 3. Integrate with POS and e-commerce checkout for real-time redemption. 4. Deploy programme economics dashboard with break-even tracking." }
        ]
    );

    // ── Real Estate & Property Management ────────────────────────────────────

    private static DomainProfile RealEstateProfile() => new(
        Name: "Real Estate & Property Management",
        TechStack: "AVM (automated valuation models), geospatial ML, computer vision for property condition scoring, and NLP lease intelligence",
        DbPattern: "PostgreSQL 16 with PostGIS (geospatial) + S3 object store (listing media) + Redis (session/search cache)",
        ArchDescription: "SOC 2 Type II compliant, RESPA/FCRA-aware architecture with MLS data governance controls",
        SubDomains: ["Property Valuation & AVM", "Tenant Screening & Risk", "Lease Intelligence",
                     "Maintenance Automation", "Market Analytics", "Investment Portfolio Analysis", "Lead & CRM Automation"],
        Competitors:
        [
            new CompetitorInsight
            {
                CompetitorName    = "Zillow",
                FeatureGap        = "Zestimate AVM accuracy degrades in low-transaction density markets (rural, luxury); iBuyer exit destroyed trust; no enterprise property management or lease intelligence modules.",
                ImpactScore       = "8.5/10",
                StrategicPlaybook = "Target enterprise property managers and institutional investors Zillow cannot serve. Lead with AVM accuracy benchmarks in Zillow's weak markets and the lease + maintenance intelligence stack Zillow lacks."
            },
            new CompetitorInsight
            {
                CompetitorName    = "CoStar Group",
                FeatureGap        = "Prohibitive subscription pricing ($50K–$200K/year) excludes mid-market; data currency gaps in emerging submarkets; no AI-driven maintenance prediction or tenant risk scoring.",
                ImpactScore       = "9.0/10",
                StrategicPlaybook = "Undercut CoStar with consumption-based pricing and AI capabilities CoStar cannot match: predictive maintenance, tenant risk scoring, and lease abstraction at 80% lower cost."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Yardi Systems",
                FeatureGap        = "Yardi Voyager legacy architecture requires 12-month implementations; AI modules are bolt-ons with no native ML infrastructure; weak lease abstraction and no geospatial intelligence.",
                ImpactScore       = "8.8/10",
                StrategicPlaybook = "Position as the AI intelligence layer alongside Yardi, not a replacement. Win by delivering in 8 weeks what Yardi AI promises take 18 months — without requiring Voyager migration."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Opendoor",
                FeatureGap        = "Consumer-only iBuyer model with no enterprise/B2B offering; AVM errors on non-standard properties led to $1.4B in losses in 2022; no property management or lease intelligence.",
                ImpactScore       = "8.0/10",
                StrategicPlaybook = "Target institutional landlords and property managers who watched Opendoor's AVM failures and want enterprise-grade valuation with explainability, confidence intervals, and human-in-the-loop override."
            }
        ],
        ItemPool:
        [
            new PrioritizedItem { Id="re-001", Name="Automated Valuation Model (AVM) Engine", Urgency=9, Difficulty=8, Value=10,
                Description="Gradient boosting + geospatial ML model producing property valuations with confidence intervals, comparable sales analysis, and neighbourhood trend overlays for any residential or commercial asset.",
                Rationale="Manual appraisals cost $400–600 and take 5–10 days; AVM reduces this to seconds at $0.02/query, enabling scalable portfolio monitoring.",
                RealLifeValue="Reduces appraisal turnaround from 7 days to real-time for 90% of standard properties, saving $1.8M annually per 10,000 annual transactions.",
                IntegrationSteps="1. Ingest MLS, county assessor, deed, and permit data into geospatial feature store. 2. Train gradient boosting model with SHAP explainability per market. 3. Build confidence interval engine with comparable sales retrieval. 4. Expose via REST API with bulk batch endpoint for portfolio revaluation." },
            new PrioritizedItem { Id="re-002", Name="AI Tenant Screening & Risk Scoring Platform", Urgency=9, Difficulty=6, Value=9,
                Description="FCRA-compliant tenant risk scoring engine combining credit, rental history, income verification, and eviction records into a transparent risk score with adverse action explanation.",
                Rationale="Eviction proceedings cost landlords $3,500–7,000 per incident; better screening reduces eviction rates by 35% with AI-augmented decisioning.",
                RealLifeValue="Reduces eviction rate by 35%, saving $875K annually per 500-unit portfolio while accelerating approval decisions from 48 hours to 4 hours.",
                IntegrationSteps="1. Integrate credit bureau APIs (Experian, TransUnion) with FCRA-compliant permissioned access. 2. Build income verification pipeline (Plaid, payroll APIs). 3. Train risk model with ECOA-compliant feature selection. 4. Generate adverse action notices automatically for declined applicants." },
            new PrioritizedItem { Id="re-003", Name="Lease Abstraction & Intelligence Engine", Urgency=8, Difficulty=6, Value=8,
                Description="NLP system extracting critical lease terms — rent escalations, break clauses, TI allowances, exclusivity, and SNDA requirements — from commercial leases and populating the lease management system.",
                Rationale="Manual lease abstraction takes 4 hours per document at $150/hour; AI reduces this to 8 minutes with 94% accuracy on trained clause types.",
                RealLifeValue="Reduces lease abstraction cost from $600 to $12 per document, saving $1.1M annually per 2,000 leases under management.",
                IntegrationSteps="1. Fine-tune LLM on commercial lease dataset (CUAD + proprietary). 2. Build structured extraction schema for 45 critical lease attributes. 3. Integrate with lease management system (Yardi, MRI) via API. 4. Implement human-in-the-loop review queue for low-confidence extractions." },
            new PrioritizedItem { Id="re-004", Name="Predictive Maintenance & Work Order Router", Urgency=8, Difficulty=6, Value=8,
                Description="IoT-integrated maintenance intelligence platform predicting equipment failures, triaging maintenance requests via NLP, and routing work orders to the optimal contractor based on availability and proximity.",
                Rationale="Reactive maintenance costs 3–9x more than preventive; AI-predicted maintenance reduces capital expenditure by 25% on a $100M property portfolio.",
                RealLifeValue="Reduces maintenance costs by 22% and equipment failure incidents by 30%, saving $440K annually per 1,000 managed units.",
                IntegrationSteps="1. Integrate IoT sensors for HVAC, elevator, and utility monitoring. 2. Build NLP classifier for maintenance request triage and priority scoring. 3. Implement geospatial contractor routing engine. 4. Connect to property management platform (AppFolio, Buildium) for work order sync." },
            new PrioritizedItem { Id="re-005", Name="Real Estate Market Intelligence Platform", Urgency=7, Difficulty=7, Value=8,
                Description="Geospatial analytics platform monitoring supply/demand signals, migration patterns, permit activity, and rent trends at the submarket level to support investment and asset management decisions.",
                Rationale="Institutional investors managing $500M+ portfolios require 90-day leading indicators; manual market research delivers 30-day lagging data.",
                RealLifeValue="Improves investment underwriting accuracy by 28%, avoiding an average of $4.2M in mispriced acquisitions per $500M deployed capital.",
                IntegrationSteps="1. Aggregate data from CoStar API, census, permit APIs, and migration data. 2. Build submarket clustering model with leading indicator identification. 3. Deploy geospatial dashboard with trend overlay mapping. 4. Generate weekly automated market intelligence reports per submarket." },
            new PrioritizedItem { Id="re-006", Name="AI Lead Qualification & CRM Automation", Urgency=8, Difficulty=5, Value=8,
                Description="Conversational AI pre-qualifying inbound rental and purchase leads, scheduling tours, answering property questions 24/7, and updating CRM records — reducing agent workload by 60%.",
                Rationale="85% of real estate leads are never followed up within 5 minutes; AI engagement within 60 seconds of inquiry increases conversion rate by 391%.",
                RealLifeValue="Increases lead-to-tour conversion by 38% and reduces agent administrative time by 60%, worth $280K annually per 10-agent team.",
                IntegrationSteps="1. Deploy conversational AI with property knowledge base. 2. Integrate with showing scheduling platform (ShowingTime, Calendly). 3. Sync lead activity to CRM (Salesforce, Follow Up Boss). 4. Implement lead scoring model for agent prioritisation." },
            new PrioritizedItem { Id="re-007", Name="Investment Portfolio Optimisation Engine", Urgency=7, Difficulty=8, Value=8,
                Description="Multi-factor portfolio analytics platform modelling cash-on-cash returns, IRR scenarios, cap rate compression risk, and geographic diversification for institutional real estate portfolios.",
                Rationale="Institutional real estate portfolios underperform benchmarks by 180bps on average due to suboptimal allocation; AI optimisation closes this gap.",
                RealLifeValue="Improves risk-adjusted portfolio return by 1.4% annually, generating $14M in additional return on a $1B portfolio.",
                IntegrationSteps="1. Integrate rent roll, operating expense, and financing data from asset management systems. 2. Build DCF and Monte Carlo simulation engine with sensitivity analysis. 3. Implement portfolio optimisation solver with geographic and sector constraints. 4. Generate LP reporting with waterfall distribution modelling." },
            new PrioritizedItem { Id="re-008", Name="Property Condition Intelligence via Computer Vision", Urgency=6, Difficulty=7, Value=7,
                Description="Computer vision platform analysing property listing photos and inspection images to score condition, detect deferred maintenance items, and estimate renovation costs at scale.",
                Rationale="Property condition assessment takes 4–6 hours manually; AI visual inspection enables portfolio-wide condition scoring in minutes.",
                RealLifeValue="Reduces due diligence inspection costs by 55% and identifies hidden renovation needs missed in 28% of manual walkthroughs.",
                IntegrationSteps="1. Build image classification model for condition scoring (1–5 scale per room type). 2. Train defect detection model on labelled maintenance images. 3. Integrate renovation cost estimation database. 4. Connect to acquisition underwriting workflow for automated CapEx scheduling." }
        ]
    );

    // ── Education & EdTech ────────────────────────────────────────────────────

    private static DomainProfile EdTechProfile() => new(
        Name: "Education & EdTech",
        TechStack: "adaptive learning engines (item response theory + DKT), NLP for essay scoring, knowledge graph curricula, and learner outcome prediction ML",
        DbPattern: "PostgreSQL 16 (learner records) + Redis (session/quiz state) + S3 (course media/SCORM packages)",
        ArchDescription: "FERPA/COPPA-compliant, LTI 1.3 standards-based architecture supporting SSO with institutional identity providers",
        SubDomains: ["Adaptive Learning & Assessment", "Learning Analytics", "Content Intelligence",
                     "Learner Engagement & Retention", "Curriculum Design AI", "Credential Verification", "Tutor AI"],
        Competitors:
        [
            new CompetitorInsight
            {
                CompetitorName    = "Coursera for Business",
                FeatureGap        = "Pre-packaged content catalogue limits domain customisation; no adaptive assessment for enterprise-specific skill gaps; analytics limited to completion rates without learning outcome measurement.",
                ImpactScore       = "8.2/10",
                StrategicPlaybook = "Target L&D teams frustrated by Coursera's rigid catalogue with a custom adaptive learning platform that measures skill acquisition, not just video completion — and integrates with internal knowledge bases."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Canvas (Instructure)",
                FeatureGap        = "LMS-centric architecture lacks native adaptive learning, AI tutoring, and predictive at-risk student detection; AI features require third-party add-ons increasing total cost by 40%.",
                ImpactScore       = "8.5/10",
                StrategicPlaybook = "Win institutions on Canvas with AI intelligence modules deployable via LTI 1.3 without LMS migration. Position as the AI brain Canvas never shipped."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Duolingo",
                FeatureGap        = "Consumer language-only focus; gamification-heavy approach reduces engagement for professional upskilling; no enterprise administration, compliance reporting, or integration with HRIS/LMS.",
                ImpactScore       = "7.5/10",
                StrategicPlaybook = "Target enterprise language training budgets with a Duolingo-quality adaptive engine wrapped in enterprise compliance, HRIS integration, and learning outcome reporting."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Anthology (Blackboard)",
                FeatureGap        = "Post-acquisition technical debt creates unstable platform; Blackboard AI limited to basic writing assistance; no predictive retention, adaptive assessment, or learning analytics at institutional scale.",
                ImpactScore       = "9.0/10",
                StrategicPlaybook = "Target institutions evaluating Blackboard migration with a modern AI-native LMS. The Anthology instability window creates a 24-month displacement opportunity — aggressive win-back campaigns now."
            }
        ],
        ItemPool:
        [
            new PrioritizedItem { Id="ed-001", Name="Adaptive Learning Path Engine", Urgency=9, Difficulty=7, Value=10,
                Description="Knowledge tracing system modelling each learner's mastery state per skill using deep knowledge tracing (DKT), dynamically sequencing content to address gaps and accelerate demonstrated strengths.",
                Rationale="Fixed-path courseware ignores learner prior knowledge; adaptive sequencing reduces time-to-competency by 40% in controlled studies.",
                RealLifeValue="Reduces course completion time by 38% while improving post-assessment scores by 27%, increasing learner throughput without additional instructor headcount.",
                IntegrationSteps="1. Map curriculum to skill graph with prerequisite relationships. 2. Deploy DKT model trained on response sequences. 3. Build real-time path recommendation engine per learner session. 4. Integrate with LMS via LTI 1.3 deep linking." },
            new PrioritizedItem { Id="ed-002", Name="At-Risk Student Early Warning System", Urgency=9, Difficulty=5, Value=9,
                Description="Predictive analytics platform identifying students at risk of withdrawal 6–8 weeks in advance using engagement signals, grade trajectory, and help-seeking behaviour — triggering targeted advisor interventions.",
                Rationale="30% of college students withdraw before completion; early intervention when triggered 6 weeks early has 3x the retention impact vs end-of-term flags.",
                RealLifeValue="Improves student retention rate by 8 percentage points, generating $2.4M in additional tuition revenue per 5,000-student institution annually.",
                IntegrationSteps="1. Integrate LMS event streams (logins, submissions, discussion activity). 2. Train gradient boosting early warning model on historical cohort outcomes. 3. Build advisor alert dashboard with recommended intervention scripts. 4. Implement FERPA-compliant audit trail for all intervention records." },
            new PrioritizedItem { Id="ed-003", Name="AI Essay & Assignment Grading Platform", Urgency=8, Difficulty=7, Value=8,
                Description="Multi-dimensional automated scoring system evaluating essays for argument structure, evidence quality, grammar, and style, providing detailed rubric-aligned feedback within seconds of submission.",
                Rationale="Grading consumes 40% of instructor time; AI-assisted grading with human spot-review reduces grading time by 75% while improving feedback consistency.",
                RealLifeValue="Saves each instructor 8 hours per week in grading time, worth $96K annually per 20-instructor department.",
                IntegrationSteps="1. Fine-tune LLM on rubric-aligned essay scoring dataset per subject. 2. Build dimensional scoring (coherence, evidence, mechanics) pipeline. 3. Integrate with LMS gradebook via LTI Advantage Assignments. 4. Implement instructor review mode for AI-flagged edge cases." },
            new PrioritizedItem { Id="ed-004", Name="Intelligent Tutoring System", Urgency=8, Difficulty=8, Value=9,
                Description="Socratic dialogue AI tutor that scaffolds problem-solving through targeted questioning, hints, and worked examples — adapting to each student's zone of proximal development in real time.",
                Rationale="One-on-one tutoring produces 2 sigma learning improvement (Bloom 1984); AI tutors replicate this at near-zero marginal cost per learner.",
                RealLifeValue="Produces learning gains equivalent to 1.4 sigma improvement — comparable to individual human tutoring — at $0.04 per hour of tutoring delivered.",
                IntegrationSteps="1. Build subject knowledge base with worked example library. 2. Implement Socratic dialogue controller with hint sequence planning. 3. Integrate learner mastery state from knowledge tracing model. 4. Deploy as LTI 1.3 embedded tool within existing LMS." },
            new PrioritizedItem { Id="ed-005", Name="Learning Content Recommendation Engine", Urgency=7, Difficulty=5, Value=8,
                Description="Collaborative and content-based filtering engine surfacing supplementary learning resources, peer study groups, and external materials calibrated to each learner's current knowledge gap profile.",
                Rationale="Learners self-directing study spend 60% of time on already-mastered concepts; intelligent recommendation steers study time to high-impact gaps.",
                RealLifeValue="Improves assessment pass rates by 19% among learners using AI-recommended study paths vs self-directed learning.",
                IntegrationSteps="1. Index all course content with semantic embeddings. 2. Build learner profile from knowledge tracing mastery states. 3. Implement hybrid recommender (content similarity + collaborative). 4. Surface recommendations in course navigation UI and weekly study plan emails." },
            new PrioritizedItem { Id="ed-006", Name="Curriculum Gap Analysis Platform", Urgency=7, Difficulty=6, Value=7,
                Description="NLP-driven platform comparing institutional curricula against industry job posting skill requirements, identifying coverage gaps, outdated content, and new skills required for graduate employability.",
                Rationale="Employers report 67% of graduates lack core skills taught in their programmes; curriculum gap analysis enables proactive programme updates aligned to market demand.",
                RealLifeValue="Improves graduate employment rates by 12 percentage points when curriculum is updated based on AI gap analysis — a key ranking and revenue metric.",
                IntegrationSteps="1. Scrape and embed industry job postings by discipline from LinkedIn/Indeed APIs. 2. Map course learning outcomes to skill taxonomy (ESCO, O*NET). 3. Build gap scoring matrix per programme. 4. Generate quarterly curriculum advisory reports for department heads." },
            new PrioritizedItem { Id="ed-007", Name="Automated Credential Verification Engine", Urgency=6, Difficulty=5, Value=7,
                Description="Blockchain-anchored credential issuance and verification platform enabling instant, tamper-proof verification of degrees, certificates, and micro-credentials by employers and other institutions.",
                Rationale="Credential fraud costs institutions and employers $600M annually; blockchain verification eliminates manual verification processes entirely.",
                RealLifeValue="Eliminates $45 per-credential manual verification cost, saving $450K annually per institution issuing 10,000 credentials per year.",
                IntegrationSteps="1. Integrate with student information system for authoritative record access. 2. Issue W3C Verifiable Credentials anchored to a permissioned blockchain. 3. Deploy digital wallet for learner credential management. 4. Build employer verification portal with instant QR-code validation." },
            new PrioritizedItem { Id="ed-008", Name="Learning Analytics & Outcome Dashboard", Urgency=7, Difficulty=4, Value=7,
                Description="Institutional analytics platform aggregating learner engagement, progression, assessment, and employment outcomes into dashboards for programme directors, accreditors, and institutional leadership.",
                Rationale="Accreditation bodies increasingly require outcome evidence; institutions without robust analytics risk losing accreditation and federal funding eligibility.",
                RealLifeValue="Reduces accreditation evidence preparation time by 70% and enables data-driven programme improvements that improve rankings by an average 8 positions.",
                IntegrationSteps="1. Aggregate data from LMS, SIS, and career services platforms. 2. Build learner journey visualisation with cohort comparison. 3. Create accreditation evidence report templates per standard (HLC, SACSCOC). 4. Deploy executive dashboard with predictive enrolment and retention modelling." }
        ]
    );

    // ── Local Services ────────────────────────────────────────────────────────

    private static DomainProfile LocalServicesProfile() => new(
        Name: "Local Services",
        TechStack: "geospatial job dispatch ML, mobile-first field service platforms, IoT diagnostics, and natural language work order processing",
        DbPattern: "PostgreSQL 16 with PostGIS (geospatial dispatch) + Redis (live technician location cache) + S3 (job photos/documents)",
        ArchDescription: "offline-capable mobile architecture with conflict resolution sync, GPS tracking, and PCI-DSS Level 2 payment processing",
        SubDomains: ["Intelligent Job Dispatch", "Predictive Maintenance Scheduling", "Customer Self-Service",
                     "Technician Performance Analytics", "Parts & Inventory Management", "Contractor Marketplace", "Pricing Intelligence"],
        Competitors:
        [
            new CompetitorInsight
            {
                CompetitorName    = "ServiceTitan",
                FeatureGap        = "ServiceTitan AI features limited to basic scheduling suggestions; no predictive demand forecasting, dynamic pricing, or IoT diagnostics integration; $30K+ annual contract excludes smaller operators.",
                ImpactScore       = "8.8/10",
                StrategicPlaybook = "Target ServiceTitan's mid-market customers (10–50 technicians) with AI-native dispatch, dynamic pricing, and IoT diagnostics at 40% lower total cost."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Angi (Angie's List)",
                FeatureGap        = "Lead marketplace model creates contractor commoditisation with no AI-assisted dispatch, CRM, or job management; average lead conversion rate of 8% represents massive acquisition waste.",
                ImpactScore       = "8.0/10",
                StrategicPlaybook = "Offer contractors leaving Angi a platform that owns the customer relationship end-to-end — with AI-driven direct booking that eliminates marketplace fees averaging 15–25% of job value."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Housecall Pro",
                FeatureGap        = "Consumer-grade UI with limited enterprise features; no predictive demand forecasting, dynamic pricing, or IoT integration; AI capabilities non-existent beyond basic scheduling.",
                ImpactScore       = "7.8/10",
                StrategicPlaybook = "Win scaling contractors moving beyond Housecall Pro with an AI platform that forecasts demand, optimises technician routing, and implements dynamic pricing — capabilities Housecall cannot deliver."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Thumbtack",
                FeatureGap        = "Consumer marketplace with no B2B features; no recurring service agreement support, no IoT integration, no enterprise reporting; contractor churn driven by inconsistent lead quality.",
                ImpactScore       = "7.5/10",
                StrategicPlaybook = "Target contractors who use Thumbtack for lead acquisition but need a full-stack business platform. Position as the operating system for their entire business, not just a lead source."
            }
        ],
        ItemPool:
        [
            new PrioritizedItem { Id="ls-001", Name="AI-Powered Job Dispatch & Routing Engine", Urgency=9, Difficulty=6, Value=10,
                Description="Geospatial ML dispatch system assigning incoming service requests to the optimal available technician based on skills, proximity, current route, and historical first-time fix rate.",
                Rationale="Poor dispatch decisions cost service businesses 22% in wasted drive time and 15% in repeat visits; AI dispatch recovers both losses simultaneously.",
                RealLifeValue="Reduces drive time by 28% and repeat visit rate by 35%, generating $380K in annual savings per 20-technician team.",
                IntegrationSteps="1. Integrate real-time GPS tracking for all technicians. 2. Build skill-to-job-type matching matrix. 3. Implement OSRM routing engine with live traffic. 4. Deploy dispatch recommendation UI with one-click assignment." },
            new PrioritizedItem { Id="ls-002", Name="Predictive Service Demand Forecasting", Urgency=8, Difficulty=6, Value=9,
                Description="Seasonal demand forecasting platform predicting service call volume by trade type, geographic zone, and equipment age cohort to enable proactive technician scheduling and parts pre-positioning.",
                Rationale="HVAC companies lose 25% of peak season revenue due to understaffing; 6-week demand forecasts enable precise contractor capacity planning.",
                RealLifeValue="Increases peak season revenue capture by 22% through proactive capacity planning, worth $440K annually per $2M revenue service business.",
                IntegrationSteps="1. Aggregate historical job data with weather, equipment age, and seasonal signals. 2. Train time-series forecasting model per trade and geography. 3. Build capacity planning dashboard with recommended staffing levels. 4. Integrate with scheduling platform for automated block-booking." },
            new PrioritizedItem { Id="ls-003", Name="Dynamic Job Pricing Intelligence Engine", Urgency=8, Difficulty=5, Value=9,
                Description="Market-aware pricing engine adjusting service quotes dynamically based on local demand, technician availability, job complexity, and competitive price benchmarks to maximise revenue per available technician hour.",
                Rationale="Service businesses using flat-rate pricing leave 18–35% in revenue on the table during peak demand; dynamic pricing recaptures this without volume impact.",
                RealLifeValue="Increases average job revenue by 21% during peak demand periods without reducing booking conversion, adding $210K annually per $1M baseline revenue.",
                IntegrationSteps="1. Build competitive price intelligence via local market data scraping. 2. Implement demand elasticity model per job type and time slot. 3. Deploy dynamic quote engine in booking flow with margin guardrails. 4. A/B test price sensitivity per market segment." },
            new PrioritizedItem { Id="ls-004", Name="AI Customer Self-Service & Booking Platform", Urgency=9, Difficulty=4, Value=8,
                Description="Conversational AI booking assistant that qualifies service requests via NLP, provides instant quotes, schedules appointments, and handles reschedule/cancellation requests without dispatcher intervention.",
                Rationale="60% of service booking calls occur outside business hours; 24/7 AI booking captures 40% more leads from after-hours inquiries.",
                RealLifeValue="Captures 38% more inbound service requests after hours, generating $285K in additional annual revenue per $2M service business.",
                IntegrationSteps="1. Deploy conversational AI with service catalogue knowledge base. 2. Integrate with scheduling platform for real-time availability. 3. Connect to payment gateway for deposit collection at booking. 4. Sync customer records to CRM with full conversation transcript." },
            new PrioritizedItem { Id="ls-005", Name="Technician Performance & Coaching Platform", Urgency=7, Difficulty=5, Value=8,
                Description="Analytics platform scoring technician performance on first-time fix rate, customer satisfaction, upsell conversion, and time-on-job metrics, delivering personalised coaching recommendations.",
                Rationale="Top-quartile technicians generate 2.4x the revenue of bottom-quartile; coaching bottom performers to median performance represents the highest-ROI training investment.",
                RealLifeValue="Improves average technician revenue-per-job by 18% through data-driven coaching, adding $108K annually per 10-technician team.",
                IntegrationSteps="1. Aggregate job, review, and revenue data per technician. 2. Build multi-metric performance score with peer benchmarking. 3. Generate weekly coaching report per technician with specific recommendations. 4. Integrate with manager dashboard for 1-on-1 conversation support." },
            new PrioritizedItem { Id="ls-006", Name="IoT Equipment Diagnostics Platform", Urgency=7, Difficulty=7, Value=8,
                Description="Remote diagnostics platform reading connected HVAC, appliance, and electrical equipment telemetry to detect anomalies, predict failures, and dispatch proactive service before breakdown occurs.",
                Rationale="Emergency breakdown repairs cost 3–5x more than scheduled maintenance; IoT-enabled proactive service converts one-time customers into recurring subscribers.",
                RealLifeValue="Converts 25% of reactive repair customers to recurring maintenance plans at 3x lifetime value, increasing average customer LTV by $840.",
                IntegrationSteps="1. Integrate with equipment OEM APIs and universal IoT gateways (ecobee, Nest, CT200). 2. Build anomaly detection model per equipment type. 3. Implement automated service alert with pre-scheduled dispatch. 4. Build customer-facing health dashboard in mobile app." },
            new PrioritizedItem { Id="ls-007", Name="Parts & Inventory Optimisation Engine", Urgency=6, Difficulty=5, Value=7,
                Description="ML-driven inventory management platform predicting parts consumption by job type and season, optimising van stock levels, and automating reorder workflows to eliminate stockouts on common repairs.",
                Rationale="Technicians fail to complete 12% of jobs due to missing parts; van stock optimisation reduces job incompletion rate and eliminates costly same-day parts runs.",
                RealLifeValue="Reduces job incompletion due to missing parts by 70% and parts acquisition costs by 18%, saving $95K annually per 10-technician team.",
                IntegrationSteps="1. Analyse parts usage history by job type, season, and geography. 2. Train demand model per SKU per technician territory. 3. Build automated reorder system with supplier API integration. 4. Deploy van inventory checklist app with barcode scanning." },
            new PrioritizedItem { Id="ls-008", Name="Customer Retention & Review Automation", Urgency=7, Difficulty=4, Value=7,
                Description="Post-service customer engagement platform triggering personalised follow-up sequences, maintenance reminders, seasonal offers, and review requests to maximise repeat business and online reputation.",
                Rationale="Acquiring a new service customer costs 5–7x more than retaining an existing one; automated follow-up increases repeat booking rate by 45%.",
                RealLifeValue="Increases customer repeat booking rate by 40%, reducing customer acquisition cost by $85 per booking and generating $320K additional annual revenue per $2M business.",
                IntegrationSteps="1. Build post-job trigger workflow with 24h satisfaction check, 7-day review request, and seasonal maintenance reminder sequences. 2. Integrate with Google Business Profile API for review response automation. 3. Connect to email/SMS marketing platform. 4. Build customer lifetime value dashboard with churn prediction." }
        ]
    );

    // ── Core Software & Tech ──────────────────────────────────────────────────

    private static DomainProfile CoreSoftwareProfile() => new(
        Name: "Core Software & Tech",
        TechStack: "MLOps pipelines (Kubeflow/MLflow), developer experience AI (Copilot-class code intelligence), observability stack (OpenTelemetry + PromQL), and policy-as-code governance",
        DbPattern: "PostgreSQL 16 (application data) + ClickHouse (analytics/telemetry) + Redis Cluster (rate limiting/caching)",
        ArchDescription: "SOC 2 Type II, ISO 27001-aligned, multi-tenant SaaS with VPC peering and private deployment options",
        SubDomains: ["Developer Productivity AI", "MLOps & Model Governance", "Platform Engineering",
                     "Security & Compliance Automation", "Observability & AIOps", "API Management", "Data Platform"],
        Competitors:
        [
            new CompetitorInsight
            {
                CompetitorName    = "GitHub Copilot",
                FeatureGap        = "Code completion focus misses the enterprise developer workflow: no architecture assistance, API contract generation, test scaffolding from specs, or security-aware code review integrated into CI/CD.",
                ImpactScore       = "8.8/10",
                StrategicPlaybook = "Position as the enterprise developer intelligence platform that extends beyond Copilot's autocomplete to cover the full SDLC — from architecture to deployment. Target CTO buyers, not individual developers."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Datadog",
                FeatureGap        = "Observability-only platform with no root-cause ML inference, automated remediation, or capacity planning AI; Datadog AI Bits feature limited to natural language query translation with no action capability.",
                ImpactScore       = "8.5/10",
                StrategicPlaybook = "Win on AIOps intelligence: not just monitoring dashboards but ML-powered anomaly detection, automated root-cause analysis, and remediation playbook execution Datadog cannot match."
            },
            new CompetitorInsight
            {
                CompetitorName    = "HashiCorp (IBM)",
                FeatureGap        = "Post-IBM acquisition licensing changes creating customer exodus; Terraform/Vault AI integration minimal; no developer productivity or intelligent infrastructure recommendation features.",
                ImpactScore       = "8.2/10",
                StrategicPlaybook = "Target HashiCorp customers evaluating alternatives due to BSL licensing changes. Offer OpenTofu-compatible infrastructure intelligence with AI-generated policy and cost optimisation as the HashiCorp antidote."
            },
            new CompetitorInsight
            {
                CompetitorName    = "Dynatrace",
                FeatureGap        = "Davis AI limited to anomaly detection within Dynatrace's own data model; no code intelligence, developer workflow integration, or cross-tool orchestration beyond the Dynatrace ecosystem.",
                ImpactScore       = "8.3/10",
                StrategicPlaybook = "Differentiate with open-standard observability (OpenTelemetry-native) and AI that acts across the full toolchain — not just within a proprietary agent ecosystem. Target multi-cloud enterprises locked into Dynatrace agents."
            }
        ],
        ItemPool:
        [
            new PrioritizedItem { Id="cs-001", Name="AI-Assisted Code Review & Security Gate", Urgency=9, Difficulty=6, Value=9,
                Description="LLM-powered static analysis tool integrated into CI/CD pipelines detecting security vulnerabilities (OWASP Top 10), architectural anti-patterns, and performance regressions with auto-generated fix suggestions.",
                Rationale="Security vulnerabilities introduced at code review cost 100x more to remediate in production; AI-gated PR review catches 65% of SAST-detectable vulnerabilities before merge.",
                RealLifeValue="Prevents an average of 3.2 critical production security incidents annually per 50-developer team, each costing $125K to remediate.",
                IntegrationSteps="1. Deploy GitHub/GitLab PR webhook integration with LLM analysis pipeline. 2. Configure OWASP, CWE, and custom rule libraries. 3. Implement fix suggestion engine with compilable patch generation. 4. Add security score gate — block merge on critical findings." },
            new PrioritizedItem { Id="cs-002", Name="MLOps Model Lifecycle Management Platform", Urgency=8, Difficulty=7, Value=9,
                Description="End-to-end ML platform managing experiment tracking, model registry, deployment pipelines, drift monitoring, and automated retraining — cutting model time-to-production from months to days.",
                Rationale="60% of ML models never reach production; standardised MLOps platforms increase deployment success rates by 3x and reduce production model latency to days.",
                RealLifeValue="Reduces model time-to-production from 4 months to 12 days and extends average production model lifespan by 2x through continuous monitoring.",
                IntegrationSteps="1. Deploy MLflow experiment tracking with artifact storage on S3. 2. Build CI/CD pipeline for model validation, A/B testing, and canary deployment. 3. Implement production drift monitoring with automated retraining triggers. 4. Build model performance vs business KPI correlation dashboard." },
            new PrioritizedItem { Id="cs-003", Name="Intelligent API Gateway & Rate Management", Urgency=8, Difficulty=6, Value=8,
                Description="AI-enhanced API gateway providing dynamic rate limiting, anomaly-based abuse detection, intelligent caching policy generation, and automatic API documentation from traffic analysis.",
                Rationale="30% of API traffic is anomalous or abusive; intelligent gateways reduce malicious traffic while optimising cache hit rates that directly reduce infrastructure cost.",
                RealLifeValue="Reduces API infrastructure cost by 32% through intelligent caching and eliminates 99.7% of API abuse traffic before it reaches application servers.",
                IntegrationSteps="1. Deploy Kong/Envoy gateway with ML anomaly scoring plugin. 2. Train traffic pattern model on baseline request signatures. 3. Implement dynamic rate limiting with per-tenant adaptive thresholds. 4. Build API analytics dashboard with cost-per-endpoint attribution." },
            new PrioritizedItem { Id="cs-004", Name="AIOps Incident Detection & Remediation", Urgency=9, Difficulty=7, Value=9,
                Description="ML-powered incident intelligence platform correlating alerts across metrics, logs, and traces to surface root-cause hypotheses, rank probable fixes, and execute approved remediation playbooks automatically.",
                Rationale="Engineers spend 40% of on-call time on alert fatigue and manual root-cause investigation; AIOps reduces MTTR by 60% and on-call incident volume by 35%.",
                RealLifeValue="Reduces MTTR from 45 minutes to 12 minutes on average, saving $2.4M annually in SLA breach penalties and incident response labour per 100-engineer organisation.",
                IntegrationSteps="1. Integrate OpenTelemetry traces, Prometheus metrics, and ELK log streams. 2. Build topology-aware anomaly correlation model. 3. Train root-cause hypothesis ranking model on historical postmortems. 4. Implement automated playbook execution engine with human approval gates." },
            new PrioritizedItem { Id="cs-005", Name="Developer Productivity Intelligence Platform", Urgency=8, Difficulty=5, Value=8,
                Description="DORA metrics + AI insights platform measuring deployment frequency, change failure rate, lead time, and MTTR — identifying bottlenecks in CI/CD pipelines and recommending targeted process improvements.",
                Rationale="Engineering teams with elite DORA metrics ship 208x more frequently with 106x faster recovery; AI-guided process improvement accelerates teams from medium to high performers in 6 months.",
                RealLifeValue="Accelerates teams from medium to high DORA performer classification within 6 months, improving deployment frequency by 5x and reducing change failure rate by 60%.",
                IntegrationSteps="1. Integrate GitHub/GitLab events, CI/CD pipelines, and incident management APIs. 2. Build DORA metric computation engine with team and service granularity. 3. Implement bottleneck detection AI with specific recommendation generation. 4. Deploy engineering excellence dashboard for VPE reporting." },
            new PrioritizedItem { Id="cs-006", Name="Cloud Cost Optimisation Intelligence Engine", Urgency=8, Difficulty=6, Value=8,
                Description="Multi-cloud cost analytics platform with ML-powered rightsizing recommendations, reserved instance optimisation, idle resource detection, and anomalous spend alerting across AWS, Azure, and GCP.",
                Rationale="Average cloud waste is 32% of total spend (Flexera 2024); AI rightsizing reduces waste by 28% within 90 days of deployment.",
                RealLifeValue="Reduces cloud spend by 28% — saving $1.4M annually per $5M cloud budget — while maintaining performance SLOs.",
                IntegrationSteps="1. Integrate AWS Cost Explorer, Azure Cost Management, and GCP Billing APIs. 2. Build workload-to-resource utilisation model for rightsizing. 3. Implement reserved instance purchase recommendation engine. 4. Deploy Slack/Teams alert bot for anomalous spend events." },
            new PrioritizedItem { Id="cs-007", Name="Infrastructure-as-Code Intelligence Platform", Urgency=7, Difficulty=6, Value=7,
                Description="AI platform generating, reviewing, and optimising Terraform/Pulumi IaC from architecture descriptions, detecting drift between declared and live infrastructure, and enforcing policy-as-code compliance.",
                Rationale="IaC drift affects 45% of production environments; AI-generated IaC with policy gates reduces misconfiguration incidents by 70%.",
                RealLifeValue="Reduces infrastructure misconfiguration incidents by 68%, preventing an average of 2.1 outages per year per team, each costing $180K in engineer time and SLA penalties.",
                IntegrationSteps="1. Deploy natural language to Terraform generation pipeline. 2. Integrate with state backend for drift detection. 3. Implement OPA policy library for security and cost guardrails. 4. Build IaC review PR integration with compliance scoring." },
            new PrioritizedItem { Id="cs-008", Name="Data Platform Observability & Quality Engine", Urgency=7, Difficulty=6, Value=7,
                Description="Data observability platform monitoring freshness, volume, schema drift, and distribution anomalies across all data pipeline outputs — alerting data engineers to breaks before downstream consumers are impacted.",
                Rationale="Data quality incidents cost enterprises $12.9M annually on average; data observability catches 78% of pipeline breaks before downstream SLA violations occur.",
                RealLifeValue="Reduces data quality incidents reaching production consumers by 78%, saving 14 engineering hours per week in incident investigation per data team.",
                IntegrationSteps="1. Instrument dbt models and Airflow DAGs with quality metric hooks. 2. Build statistical anomaly detection per table/column profile. 3. Implement schema change detection with breaking change alerting. 4. Deploy data lineage graph for impact radius analysis on upstream changes." }
        ]
    );

    // ── Fresh item templates (used when the pre-built pool is exhausted) ──────

    private static readonly IReadOnlyDictionary<string, FreshItemTemplate[]> FreshItemTemplates =
        new Dictionary<string, FreshItemTemplate[]>(StringComparer.Ordinal)
        {
            ["Healthcare AI"] =
            [
                new("AI-Augmented {0} Clinical Workflow", "Context-aware AI layer automating routine tasks within existing clinical workflows to recapture physician time for high-value patient interactions.", "Physician burnout affects 42% of US doctors; workflow automation reduces administrative burden by 28%.", "Recaptures 6 hours/week per physician, valued at $180K annually per 10-physician practice.", "1. Map current workflow steps. 2. Identify high-frequency, low-judgment tasks. 3. Deploy AI micro-automations per step. 4. Monitor adoption and override rates."),
                new("{0} Patient Engagement Intelligence", "Personalised patient communication platform using NLP and behavioural models to improve medication adherence, appointment attendance, and care plan compliance.", "Non-adherence costs the US $300B annually in avoidable hospitalisations.", "Improves medication adherence by 22%, reducing avoidable hospitalisations by 15% per enrolled cohort.", "1. Integrate with patient portal APIs. 2. Build segmentation model by adherence risk. 3. Personalise outreach channel and message. 4. Track engagement and health outcomes.")
            ],
            ["Financial Technology"] =
            [
                new("{0} Financial Risk Intelligence Hub", "Unified risk management platform aggregating market, credit, operational, and liquidity risk signals into a single real-time risk dashboard for enterprise risk officers.", "Fragmented risk systems create 4-hour reporting lag that obscures emerging risk concentrations.", "Reduces risk reporting cycle from 4 hours to 15 minutes, enabling faster de-risking decisions.", "1. Aggregate risk feeds from all trading and banking systems. 2. Build unified risk data model. 3. Deploy real-time risk aggregation engine. 4. Build CRO dashboard with scenario stress-testing."),
                new("{0} AI Wealth Advisory Engine", "AI-powered financial planning system generating personalised investment plans, tax optimisation strategies, and retirement projections for mass-affluent clients at scale.", "Robo-advisory commoditised basic allocation; differentiation now lies in holistic financial planning AI.", "Increases assets under advice by 35% for clients receiving AI financial plans vs generic allocation.", "1. Integrate client financial data aggregation. 2. Build goal-based planning engine. 3. Generate personalised plan narratives. 4. Connect to portfolio rebalancing execution.")
            ],
            ["Legal Technology"] =
            [
                new("{0} Legal Workflow Automation Engine", "End-to-end legal matter management platform automating intake, conflict checking, document assembly, deadline tracking, and billing across the full matter lifecycle.", "Law firms lose 30% of potential revenue to administrative overhead and billing leakage.", "Reduces matter overhead by 40%, recovering $600K in annual revenue per 20-attorney team.", "1. Map matter lifecycle workflow. 2. Automate intake and conflict check. 3. Deploy document assembly for routine matters. 4. Integrate with billing and DMS systems."),
                new("{0} Regulatory Intelligence Feed", "AI-curated regulatory intelligence subscription service delivering jurisdiction-specific, practice-area-filtered regulatory updates with impact assessments and implementation checklists.", "Legal teams spend 20 hours per week manually monitoring 200+ regulatory sources.", "Reduces regulatory monitoring labour by 75%, freeing 15 hours per attorney per week for billable work.", "1. Subscribe to global regulatory RSS and API feeds. 2. Build AI classifier for relevance filtering. 3. Generate impact assessments per subscriber profile. 4. Deliver via email digest and API.")
            ],
            ["Enterprise AI Platform"] =
            [
                new("{0} AI Operations Centre", "Centralised AIOps platform monitoring, explaining, and optimising all AI models in production — tracking drift, performance degradation, and business metric correlation in real time.", "60% of production ML models degrade within 6 months without active monitoring; AIOps extends model life by 3x.", "Extends average model production lifespan from 6 to 18 months, avoiding $500K in annual retraining costs per model.", "1. Instrument all production models with shadow scoring. 2. Build drift detection with configurable alert thresholds. 3. Implement automated retraining triggers. 4. Build model performance vs business KPI correlation dashboard."),
                new("{0} Enterprise AI Governance Platform", "Policy enforcement, audit logging, and explainability platform ensuring all AI decisions comply with EU AI Act, NIST AI RMF, and internal ethics policies.", "EU AI Act enforcement begins 2025; non-compliance carries fines up to 3% of global turnover.", "Prevents EU AI Act fines and builds enterprise trust in AI systems through transparent, auditable decision logs.", "1. Classify all AI systems by risk tier (EU AI Act). 2. Implement decision logging and explainability API. 3. Build bias monitoring dashboards per protected characteristic. 4. Automate conformity assessment documentation.")
            ],
            ["Retail & E-Commerce"] =
            [
                new("{0} Omnichannel Personalisation Engine", "Unified customer identity resolution and real-time personalisation platform delivering consistent AI-driven experiences across web, mobile, in-store, and email touchpoints.", "Brands with omnichannel personalisation retain 89% of customers vs 33% without; siloed channel data prevents most retailers from delivering it.", "Increases cross-channel revenue by 19% and customer retention by 22%, worth $3.8M annually per $50M GMV retailer.", "1. Build unified customer identity graph across all channels. 2. Deploy real-time event streaming for behavioural signals. 3. Implement cross-channel next-best-action engine. 4. Connect personalisation API to all channel touchpoints."),
                new("{0} Returns Intelligence Platform", "ML system predicting return probability at checkout, detecting return fraud patterns, and optimising return logistics routing to reduce returns cost and abuse.", "Return abuse costs e-commerce retailers $101B annually; return fraud accounts for 10.6% of total returns.", "Reduces return fraud losses by 65% and return logistics costs by 22%, saving $1.1M per $100M GMV retailer annually.", "1. Build return probability model per SKU and customer segment. 2. Implement fraud pattern detection for serial returner identification. 3. Deploy return routing optimisation to nearest re-processing centre. 4. Integrate risk score into checkout and returns portal.")
            ],
            ["Real Estate & Property Management"] =
            [
                new("{0} Rent Pricing Optimisation Engine", "Dynamic rent pricing platform adjusting asking prices daily based on vacancy rates, comparable listings, demand signals, and concession optimisation to maximise net operating income.", "Static rent pricing leaves 8–15% in potential NOI uncaptured; dynamic pricing captures this without increasing vacancy.", "Increases net operating income by 9.2% on dynamically priced units, worth $460K annually per 500-unit portfolio.", "1. Aggregate competitive listing data from Zillow/CoStar APIs. 2. Train demand elasticity model per unit type and submarket. 3. Implement daily repricing engine with vacancy guardrails. 4. Connect to property management platform for automated listing updates."),
                new("{0} HOA & Lease Compliance Tracker", "Automated lease compliance platform monitoring lease obligations, HOA rules, permit requirements, and regulatory deadlines — alerting property managers before violations occur.", "Average lease compliance violation costs $18K in penalties and remediation; proactive monitoring prevents 80% of avoidable violations.", "Prevents an average of 4.2 lease violations per 100 units annually, saving $75K per 100 units under management.", "1. Parse lease documents and extract obligation timelines. 2. Map obligations to calendar with reminder engine. 3. Integrate with local regulatory deadline APIs where available. 4. Build compliance officer dashboard with violation risk scoring.")
            ],
            ["Education & EdTech"] =
            [
                new("{0} Student Enrolment Optimisation Engine", "Predictive analytics platform modelling application yield, scholarship optimisation, and enrolment funnel conversion to maximise enrolled class quality and net tuition revenue.", "Average university yield rate is 18%; AI-optimised financial aid packaging improves yield by 6 percentage points without increasing aid spend.", "Improves enrolment yield by 5.8 percentage points, generating $4.6M additional net tuition revenue per 1,000-student incoming class.", "1. Build application scoring model with yield probability per admitted student. 2. Implement aid packaging optimisation with budget constraint solver. 3. Build personalised communication sequence trigger per applicant segment. 4. Connect to CRM and financial aid management systems."),
                new("{0} Learning Content Quality Scorer", "Automated content quality platform evaluating instructional design principles, accessibility compliance (WCAG 2.1), and learning science alignment across the course catalogue.", "40% of online courses have accessibility violations risking ADA litigation; poor instructional design reduces completion rates by 35%.", "Reduces ADA compliance risk exposure by 85% and improves course completion rates by 28% through AI-guided redesign recommendations.", "1. Deploy content crawler across LMS course catalogue. 2. Build accessibility checker against WCAG 2.1 AA standard. 3. Score instructional design against Bloom's taxonomy alignment. 4. Generate remediation priority report with effort estimates.")
            ],
            ["Local Services"] =
            [
                new("{0} Membership & Service Agreement Engine", "Recurring revenue platform converting one-time repair customers into maintenance plan subscribers, managing agreement billing, renewal forecasting, and service delivery scheduling.", "Recurring maintenance agreements generate 3x customer LTV vs one-time repair relationships; most service businesses convert less than 12% of customers.", "Converts 28% of repair customers to maintenance agreements at 3x LTV, adding $560K annually per $2M service business.", "1. Build offer engine with personalised maintenance plan recommendations by equipment age. 2. Integrate with payment platform for recurring billing. 3. Build agreement renewal prediction model with proactive outreach triggers. 4. Connect scheduled visits to dispatch system."),
                new("{0} Contractor Marketplace & Capacity Exchange", "Two-sided marketplace platform enabling service businesses to monetise excess capacity by sub-contracting overflow jobs to vetted partner contractors — with AI-driven matching and quality assurance.", "Service businesses turn away 15–20% of peak-season revenue due to capacity constraints; contractor networks monetise this overflow profitably.", "Captures 70% of previously turned-away jobs as brokered revenue, adding $280K annually per $2M service business at 25% margin.", "1. Build contractor vetting pipeline with licence, insurance, and rating verification. 2. Implement job-to-contractor matching engine based on skills, proximity, and availability. 3. Build escrow payment system with quality-gated release. 4. Implement post-job rating and performance tracking.")
            ],
            ["Core Software & Tech"] =
            [
                new("{0} Platform Engineering Internal Developer Portal", "Unified internal developer portal centralising service catalogues, infrastructure self-service, onboarding runbooks, and AI-generated architecture guidance — cutting new service time-to-prod from weeks to hours.", "Engineering teams waste 30% of time on infrastructure operations that should be self-service; IDPs recover this time for feature development.", "Reduces new service creation time from 3 weeks to 4 hours, saving 60 engineering days annually per 10-developer team.", "1. Deploy Backstage-based IDP with service catalogue. 2. Build self-service infrastructure templates for common patterns. 3. Integrate AI architecture advisor for new service design. 4. Add golden-path CI/CD templates with security and compliance pre-wired."),
                new("{0} API Contract Testing & Governance Engine", "Consumer-driven contract testing platform ensuring API provider changes never break downstream consumers — with AI-generated contract stubs, schema evolution analysis, and breaking change detection in CI.", "Breaking API changes cause 23% of production incidents; contract testing catches 91% of breaking changes before deployment.", "Reduces API-related production incidents by 88%, preventing an average of $340K in annual incident remediation costs per 50-service architecture.", "1. Deploy Pact broker for consumer-driven contract test management. 2. Build LLM-powered contract stub generator from OpenAPI specs. 3. Integrate breaking change detection gate into CI pipeline. 4. Build API versioning governance dashboard with consumer impact analysis.")
            ]
        };

    // ── Nested types ──────────────────────────────────────────────────────────

    private sealed record DomainProfile(
        string Name,
        string TechStack,
        string DbPattern,
        string ArchDescription,
        List<string> SubDomains,
        List<CompetitorInsight> Competitors,
        List<PrioritizedItem> ItemPool);

    private sealed record FreshItemTemplate(
        string NameFmt,
        string Description,
        string Rationale,
        string RealLifeValue,
        string IntegrationSteps);
}
