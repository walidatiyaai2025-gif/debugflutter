using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record MachineCapabilityRequest(
    int CpuCores,
    double MemoryGb,
    double FreeDiskGb,
    string Architecture,
    string OperatingSystem);

public sealed record MachineCapabilityDecision(
    int CpuCores,
    double MemoryGb,
    double FreeDiskGb,
    string Architecture,
    string OperatingSystem,
    bool Constrained,
    int RecommendedParallelism,
    int CapabilityScore,
    string ReasonCode,
    string Fingerprint);

public static class MachineCapabilityPolicy
{
    public static MachineCapabilityDecision Evaluate(MachineCapabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CpuCores is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(request), "CPU core count is outside the supported range.");
        if (!double.IsFinite(request.MemoryGb) || request.MemoryGb <= 0 || request.MemoryGb > 16384) throw new ArgumentOutOfRangeException(nameof(request), "Memory capacity is outside the supported range.");
        if (!double.IsFinite(request.FreeDiskGb) || request.FreeDiskGb < 0 || request.FreeDiskGb > 1_000_000) throw new ArgumentOutOfRangeException(nameof(request), "Free disk capacity is outside the supported range.");

        var architecture = NormalizeArchitecture(request.Architecture);
        var os = NormalizeOperatingSystem(request.OperatingSystem);
        var constrained = request.CpuCores < 4 || request.MemoryGb < 8 || request.FreeDiskGb < 10;
        var memoryParallelism = Math.Max(1, (int)Math.Floor(request.MemoryGb / 2));
        var recommended = Math.Clamp(Math.Min(request.CpuCores, memoryParallelism), 1, 8);
        if (constrained) recommended = Math.Min(recommended, 2);

        var cpuScore = Math.Min(request.CpuCores / 8d, 1d) * 30d;
        var memoryScore = Math.Min(request.MemoryGb / 16d, 1d) * 40d;
        var diskScore = Math.Min(request.FreeDiskGb / 50d, 1d) * 30d;
        var score = Math.Clamp((int)Math.Round(cpuScore + memoryScore + diskScore, MidpointRounding.AwayFromZero), 0, 100);
        var reason = constrained ? "machine-constrained" : "machine-capable";
        var canonical = string.Join('|', request.CpuCores, request.MemoryGb.ToString("0.###", CultureInfo.InvariantCulture), request.FreeDiskGb.ToString("0.###", CultureInfo.InvariantCulture), architecture, os, constrained, recommended, score, reason);
        return new MachineCapabilityDecision(request.CpuCores, request.MemoryGb, request.FreeDiskGb, architecture, os, constrained, recommended, score, reason, Hash(canonical));
    }

    public static string NormalizeArchitecture(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant() switch
        {
            "x64" or "amd64" or "x86_64" => "x64",
            "arm64" or "aarch64" => "arm64",
            "x86" or "i386" or "i686" => "x86",
            _ => throw new ArgumentException("Machine architecture is unsupported.", nameof(value))
        };
    }

    public static string NormalizeOperatingSystem(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl)) throw new ArgumentException("Operating-system identity contains control characters.", nameof(value));
        var normalized = string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        if (normalized.Length > 128) throw new ArgumentOutOfRangeException(nameof(value), "Operating-system identity is too long.");
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
