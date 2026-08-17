namespace MeridianStudio.API.Domain.Models;

/// <summary>
/// Represents a market gap or competitive weakness identified during research.
/// </summary>
public sealed record CompetitorInsight
{
    public required string CompetitorName { get; init; }
    public required string FeatureGap { get; init; }
    public required string ImpactScore { get; init; }
    public required string StrategicPlaybook { get; init; }

    public static CompetitorInsight Create(
        string competitorName,
        string featureGap,
        string impactScore,
        string strategicPlaybook)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(competitorName, nameof(competitorName));
        ArgumentException.ThrowIfNullOrWhiteSpace(featureGap, nameof(featureGap));
        ArgumentException.ThrowIfNullOrWhiteSpace(impactScore, nameof(impactScore));
        ArgumentException.ThrowIfNullOrWhiteSpace(strategicPlaybook, nameof(strategicPlaybook));

        return new CompetitorInsight
        {
            CompetitorName = competitorName,
            FeatureGap = featureGap,
            ImpactScore = impactScore,
            StrategicPlaybook = strategicPlaybook
        };
    }
}
