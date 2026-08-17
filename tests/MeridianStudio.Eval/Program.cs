// MeridianStudio golden-set eval harness.
//
// Runs a small set of graded briefs (briefs.json) against a RUNNING MeridianStudio.API and
// applies deterministic graders to each generated document — anchor phrases present, mandatory
// Mermaid diagram present, FactChecked flag set, and the [REQUIRED:] placeholder count within a
// ceiling. Exits non-zero if any brief fails, so it can gate prompt changes in CI.
//
// Usage:
//   dotnet run --project tests/MeridianStudio.Eval -- [apiBaseUrl]
//   (defaults to $MERIDIAN_API or http://localhost:5000)
//
// The API must be running with at least one live LLM key configured for FactChecked to pass;
// against the offline heuristic engine, set expectFactChecked=false in briefs.json.

using System.Net.Http.Json;
using System.Text.Json.Nodes;

var baseUrl = (args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("MERIDIAN_API"))
    ?? "http://localhost:5000";
baseUrl = baseUrl.TrimEnd('/');

var briefsPath = Path.Combine(AppContext.BaseDirectory, "briefs.json");
if (!File.Exists(briefsPath))
{
    Console.Error.WriteLine($"briefs.json not found at {briefsPath}");
    return 2;
}

var briefs = JsonNode.Parse(await File.ReadAllTextAsync(briefsPath))!.AsArray();
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

int totalChecks = 0, passedChecks = 0, failedBriefs = 0;
Console.WriteLine($"Meridian eval → {baseUrl}   ({briefs.Count} briefs)\n");

foreach (var b in briefs)
{
    var name    = b!["name"]!.GetValue<string>();
    var request = b["request"]!;

    string content, modelUsed;
    bool factChecked;
    int provSourceCount = 0, criterionScoreCount = 0;
    double provConfidence = 0;
    bool hasProvenance = false;
    try
    {
        var resp = await http.PostAsJsonAsync($"{baseUrl}/api/generate-document", request);
        resp.EnsureSuccessStatusCode();
        var doc = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        content     = doc["content"]?.GetValue<string>() ?? "";
        factChecked = doc["factChecked"]?.GetValue<bool>() ?? false;
        modelUsed   = doc["modelUsed"]?.GetValue<string>() ?? "?";

        // A3 provenance + A1 criterion scores (present on the new output shape).
        if (doc["provenance"] is JsonObject prov)
        {
            hasProvenance  = true;
            provSourceCount = prov["sourceCount"]?.GetValue<int>() ?? 0;
            provConfidence  = prov["confidence"]?.GetValue<double>() ?? 0;
        }
        if (doc["criterionScores"] is JsonObject cs) criterionScoreCount = cs.Count;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ {name}: request failed — {ex.Message}\n");
        failedBriefs++;
        continue;
    }

    var lower   = content.ToLowerInvariant();
    var results = new List<(string Check, bool Pass, string Detail)>();

    foreach (var anchor in b["expectAnchors"]?.AsArray() ?? [])
    {
        var a = anchor!.GetValue<string>();
        results.Add(($"contains '{a}'", lower.Contains(a.ToLowerInvariant()), ""));
    }

    if (b["expectMermaid"]?.GetValue<bool>() == true)
        results.Add(("has Mermaid diagram", content.Contains("```mermaid", StringComparison.OrdinalIgnoreCase), ""));

    if (b["expectFactChecked"]?.GetValue<bool>() == true)
        results.Add(("fact-checked", factChecked, $"model={modelUsed}"));

    var maxPlaceholders = b["maxRequiredPlaceholders"]?.GetValue<int>() ?? int.MaxValue;
    var placeholderCount = CountOccurrences(content, "[REQUIRED:");
    results.Add(($"<= {maxPlaceholders} [REQUIRED] placeholders", placeholderCount <= maxPlaceholders, $"found {placeholderCount}"));

    // ── New quality-signal graders (all opt-in per brief) ──────────────────────

    // Citation density: [S#] citations per 1000 words must clear the floor.
    if (b["expectMinCitationDensity"]?.GetValue<double>() is double minDensity)
    {
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var citations = CountCitations(content);
        var density = words > 0 ? citations * 1000.0 / words : 0;
        results.Add(($">= {minDensity:0.0} citations/1k words", density >= minDensity, $"{density:0.0} ({citations} in {words}w)"));
    }

    // Provenance envelope present (A3).
    if (b["expectProvenance"]?.GetValue<bool>() == true)
        results.Add(("has provenance", hasProvenance, $"confidence={provConfidence:0.00}"));

    // Minimum grounding sources (A2/A3).
    if (b["expectMinSources"]?.GetValue<int>() is int minSources)
        results.Add(($">= {minSources} sources", provSourceCount >= minSources, $"found {provSourceCount}"));

    // Per-criterion scores present (A1) — count should match the selected criteria.
    if (b["expectScoredCriteria"]?.GetValue<bool>() == true)
        results.Add(("has criterion scores", criterionScoreCount > 0, $"{criterionScoreCount} scored"));

    var briefPass = results.All(r => r.Pass);
    if (!briefPass) failedBriefs++;

    Console.WriteLine($"{(briefPass ? "✓" : "✗")} {name}   ({content.Length} chars, model={modelUsed})");
    foreach (var (check, pass, detail) in results)
    {
        totalChecks++;
        if (pass) passedChecks++;
        Console.WriteLine($"      {(pass ? "pass" : "FAIL")}  {check}{(string.IsNullOrEmpty(detail) ? "" : $"   [{detail}]")}");
    }
    Console.WriteLine();
}

Console.WriteLine($"Checks: {passedChecks}/{totalChecks} passed · Briefs failed: {failedBriefs}/{briefs.Count}");
return failedBriefs == 0 ? 0 : 1;

static int CountOccurrences(string haystack, string needle)
{
    int count = 0, idx = 0;
    while ((idx = haystack.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
    {
        count++;
        idx += needle.Length;
    }
    return count;
}

// Counts inline source citations of the form [S1], [S12], [S3, S4] etc.
static int CountCitations(string content)
    => System.Text.RegularExpressions.Regex.Matches(content, @"\[S\d+").Count;
