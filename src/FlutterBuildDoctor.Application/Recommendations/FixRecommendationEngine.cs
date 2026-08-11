using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Recommendations;

public enum FixRisk
{
    Safe = 0,
    Risky = 1,
    Destructive = 2
}

public sealed record FixRecommendation(
    string Id,
    string Title,
    int Confidence,
    FixRisk Risk,
    string? SourceProblemCode = null);

public sealed record RankedFixRecommendation(
    string Id,
    string Title,
    int Confidence,
    FixRisk Risk,
    bool RequiresConfirmation,
    string? SourceProblemCode,
    int Rank);

public sealed record FixRecommendationResult(
    IReadOnlyList<RankedFixRecommendation> Recommendations,
    string Fingerprint);

public static partial class FixRecommendationEngine
{
    public const int DefaultMaxRecommendations = 10;
    public const int MaxRecommendations = 50;

    public static FixRecommendationResult Rank(
        IEnumerable<FixRecommendation> recommendations,
        int maxRecommendations = DefaultMaxRecommendations)
    {
        ArgumentNullException.ThrowIfNull(recommendations);
        var limit = Math.Clamp(maxRecommendations, 1, MaxRecommendations);

        var normalized = recommendations
            .Select(Normalize)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => item.Risk)
                .ThenByDescending(item => item.Confidence)
                .ThenBy(item => item.Title, StringComparer.Ordinal)
                .First())
            .OrderBy(item => item.Risk)
            .ThenByDescending(item => item.Confidence)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select((item, index) => new RankedFixRecommendation(
                item.Id,
                item.Title,
                item.Confidence,
                item.Risk,
                item.Risk == FixRisk.Destructive,
                item.SourceProblemCode,
                index + 1))
            .ToArray();

        var fingerprint = ComputeFingerprint(normalized);
        return new FixRecommendationResult(normalized, fingerprint);
    }

    public static string NormalizeTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var normalized = WhitespaceRegex().Replace(title.Trim(), " ");
        if (normalized.Length > 160)
        {
            normalized = normalized[..160].TrimEnd();
        }

        return normalized;
    }

    private static FixRecommendation Normalize(FixRecommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        var id = NormalizeId(recommendation.Id);
        var title = NormalizeTitle(recommendation.Title);
        var confidence = Math.Clamp(recommendation.Confidence, 0, 100);
        var sourceProblemCode = string.IsNullOrWhiteSpace(recommendation.SourceProblemCode)
            ? null
            : recommendation.SourceProblemCode.Trim().ToLowerInvariant();
        return recommendation with
        {
            Id = id,
            Title = title,
            Confidence = confidence,
            SourceProblemCode = sourceProblemCode
        };
    }

    private static string NormalizeId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var normalized = id.Trim().ToLowerInvariant();
        if (normalized.Length > 96 || normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Recommendation identity must be compact and whitespace-free.", nameof(id));
        }

        return normalized;
    }

    private static string ComputeFingerprint(IEnumerable<RankedFixRecommendation> recommendations)
    {
        var canonical = string.Join('|', recommendations.Select(item => string.Join(':',
            item.Id,
            item.Title,
            item.Confidence,
            item.Risk,
            item.RequiresConfirmation,
            item.SourceProblemCode ?? string.Empty,
            item.Rank)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
