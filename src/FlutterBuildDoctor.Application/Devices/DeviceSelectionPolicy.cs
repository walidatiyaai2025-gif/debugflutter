using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Devices;

public enum DevicePlatform
{
    Unknown = 0,
    Android = 1,
    Ios = 2,
    Windows = 3,
    Web = 4
}

public enum DeviceKind
{
    Physical = 0,
    Emulator = 1
}

public sealed record DeviceCandidate(
    string Id,
    DevicePlatform Platform,
    DeviceKind Kind,
    bool IsBooted,
    bool IsCompatible = true);

public sealed record DeviceSelectionRequest(
    DevicePlatform TargetPlatform,
    string? RequestedDeviceId = null,
    int MaxCandidates = DeviceSelectionPolicy.DefaultMaxCandidates);

public sealed record DeviceSelectionDecision(
    DeviceCandidate? Selected,
    IReadOnlyList<DeviceCandidate> Candidates,
    string ReasonCode,
    string Fingerprint);

public static class DeviceSelectionPolicy
{
    public const int DefaultMaxCandidates = 32;
    public const int MaxCandidates = 64;

    public static DeviceSelectionDecision Select(
        DeviceSelectionRequest request,
        IEnumerable<DeviceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);
        if (request.TargetPlatform == DevicePlatform.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Target platform must be known.");
        }

        var normalized = candidates.Select(NormalizeCandidate).ToArray();
        if (normalized.Select(candidate => candidate.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new ArgumentException("Duplicate device identifiers are not allowed.", nameof(candidates));
        }

        var limit = Math.Clamp(request.MaxCandidates, 1, MaxCandidates);
        var ordered = normalized
            .OrderBy(candidate => CandidateRank(candidate, request.TargetPlatform))
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        var requestedId = NormalizeOptionalId(request.RequestedDeviceId);
        DeviceCandidate? selected;
        string reason;

        if (requestedId is not null)
        {
            selected = normalized.SingleOrDefault(candidate => candidate.Id.Equals(requestedId, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                return BuildDecision(null, ordered, "requested-not-found", request.TargetPlatform, requestedId);
            }

            if (selected.Platform != request.TargetPlatform || !selected.IsCompatible)
            {
                throw new InvalidOperationException("Requested device is incompatible with the target platform.");
            }

            reason = "requested";
        }
        else
        {
            selected = ordered.FirstOrDefault(candidate => candidate.Platform == request.TargetPlatform && candidate.IsCompatible && candidate.IsBooted);
            if (selected is not null)
            {
                reason = "booted-compatible";
            }
            else
            {
                selected = ordered.FirstOrDefault(candidate => candidate.Platform == request.TargetPlatform && candidate.IsCompatible && candidate.Kind == DeviceKind.Emulator);
                if (selected is not null)
                {
                    reason = "emulator-fallback";
                }
                else
                {
                    selected = ordered.FirstOrDefault(candidate => candidate.Platform == request.TargetPlatform && candidate.IsCompatible);
                    reason = selected is null ? "no-compatible-device" : "compatible-fallback";
                }
            }
        }

        return BuildDecision(selected, ordered, reason, request.TargetPlatform, requestedId);
    }

    public static string NormalizeId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var normalized = id.Trim().ToLowerInvariant();
        if (normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Device identity cannot contain whitespace.", nameof(id));
        }

        return normalized;
    }

    private static DeviceCandidate NormalizeCandidate(DeviceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Platform == DevicePlatform.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(candidate), "Candidate platform must be known.");
        }

        return candidate with { Id = NormalizeId(candidate.Id) };
    }

    private static string? NormalizeOptionalId(string? id) => string.IsNullOrWhiteSpace(id) ? null : NormalizeId(id);

    private static int CandidateRank(DeviceCandidate candidate, DevicePlatform targetPlatform)
    {
        if (candidate.Platform != targetPlatform || !candidate.IsCompatible)
        {
            return 100;
        }

        if (candidate.IsBooted && candidate.Kind == DeviceKind.Physical)
        {
            return 0;
        }

        if (candidate.IsBooted)
        {
            return 1;
        }

        if (candidate.Kind == DeviceKind.Emulator)
        {
            return 2;
        }

        return 3;
    }

    private static DeviceSelectionDecision BuildDecision(
        DeviceCandidate? selected,
        IReadOnlyList<DeviceCandidate> candidates,
        string reasonCode,
        DevicePlatform targetPlatform,
        string? requestedId)
    {
        var canonicalCandidates = candidates.Select(candidate => string.Join(':',
            candidate.Id,
            candidate.Platform,
            candidate.Kind,
            candidate.IsBooted ? "booted" : "stopped",
            candidate.IsCompatible ? "compatible" : "incompatible"));
        var canonical = string.Join('|', canonicalCandidates.Prepend($"{targetPlatform}:{requestedId ?? string.Empty}:{reasonCode}:{selected?.Id ?? string.Empty}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new DeviceSelectionDecision(selected, candidates, reasonCode, fingerprint);
    }
}
