using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Repositories;

public enum CloneMode
{
    Shallow,
    Full
}

public sealed record ClonePlanRequest(
    string RepositoryUrl,
    string DestinationName,
    string? Branch = null,
    int? Depth = null,
    CloneMode Mode = CloneMode.Shallow,
    bool DestinationExists = false,
    bool DestinationIsRepository = false,
    bool DestinationIsEmpty = true);

public sealed record ClonePlanDecision(
    bool Allowed,
    bool ReuseExisting,
    string RepositoryUrl,
    string DestinationName,
    string Branch,
    int Depth,
    CloneMode Mode,
    IReadOnlyList<string> Arguments,
    string ReasonCode,
    string Fingerprint);

public static partial class ClonePlanPolicy
{
    public const int DefaultDepth = 50;
    public const int MaxDepth = 1000;

    public static ClonePlanDecision Plan(ClonePlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var url = NormalizeUrl(request.RepositoryUrl);
        var destination = NormalizeDestination(request.DestinationName);
        var branch = NormalizeRef(request.Branch);
        var depth = request.Mode == CloneMode.Full ? 0 : Math.Clamp(request.Depth ?? DefaultDepth, 1, MaxDepth);

        var reuse = request.DestinationExists && request.DestinationIsRepository;
        var allowed = reuse || !request.DestinationExists || request.DestinationIsEmpty;
        var reason = reuse ? "reuse-existing-repository" : allowed ? "fresh-clone" : "destination-not-empty";

        var arguments = new List<string> { "clone" };
        if (request.Mode == CloneMode.Shallow)
        {
            arguments.Add("--depth");
            arguments.Add(depth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        arguments.Add("--branch");
        arguments.Add(branch);
        arguments.Add("--");
        arguments.Add(url);
        arguments.Add(destination);

        var fingerprint = Hash(string.Join('|', url, destination, branch, depth, request.Mode, reuse, allowed, reason));
        return new ClonePlanDecision(allowed, reuse, url, destination, branch, depth, request.Mode, arguments, reason, fingerprint);
    }

    public static string NormalizeUrl(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("Clone source must be an absolute HTTPS repository URL.", nameof(value));
        }

        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/');
    }

    public static string NormalizeDestination(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = DestinationInvalidRegex().Replace(value.Trim(), "-").Trim('-', '.');
        if (normalized.Length == 0 || normalized is "." or "..")
        {
            throw new ArgumentException("Destination directory name is invalid.", nameof(value));
        }
        return normalized;
    }

    public static string NormalizeRef(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "main" : value.Trim();
        if (normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains("@{", StringComparison.Ordinal)
            || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Branch/ref contains unsafe syntax.", nameof(value));
        }
        return normalized;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("[^A-Za-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex DestinationInvalidRegex();
}
