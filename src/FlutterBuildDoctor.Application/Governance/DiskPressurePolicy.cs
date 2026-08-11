using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public enum DiskPressureLevel
{
    Healthy,
    Warning,
    Critical
}

public sealed record DiskPressureDecision(
    string VolumeIdentity,
    long TotalBytes,
    long FreeBytes,
    double FreePercent,
    int WarningPercent,
    int CriticalPercent,
    DiskPressureLevel Level,
    long ReclaimTargetBytes,
    string ReasonCode,
    string Fingerprint);

public static class DiskPressurePolicy
{
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._:-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DiskPressureDecision Evaluate(
        string volumeIdentity,
        long totalBytes,
        long freeBytes,
        int warningPercent = 15,
        int criticalPercent = 5)
    {
        var volume = NormalizeIdentity(volumeIdentity);
        if (totalBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes));
        }

        if (freeBytes < 0 || freeBytes > totalBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(freeBytes));
        }

        var warning = Math.Clamp(warningPercent, 5, 50);
        var critical = Math.Clamp(criticalPercent, 1, 49);
        if (critical >= warning)
        {
            throw new ArgumentException("Critical disk threshold must be below warning threshold.", nameof(criticalPercent));
        }

        var freePercent = Math.Round(freeBytes * 100d / totalBytes, 2, MidpointRounding.AwayFromZero);
        var level = freePercent <= critical
            ? DiskPressureLevel.Critical
            : freePercent <= warning
                ? DiskPressureLevel.Warning
                : DiskPressureLevel.Healthy;

        var healthyTargetPercent = Math.Min(95, warning + 5);
        var targetFreeBytes = (long)Math.Ceiling(totalBytes * healthyTargetPercent / 100d);
        var reclaim = Math.Max(0L, targetFreeBytes - freeBytes);
        var reason = level switch
        {
            DiskPressureLevel.Critical => "disk-pressure-critical",
            DiskPressureLevel.Warning => "disk-pressure-warning",
            _ => "disk-pressure-healthy"
        };

        var canonical = $"{volume}|{totalBytes}|{freeBytes}|{freePercent:F2}|{warning}|{critical}|{level}|{reclaim}|{reason}";
        return new DiskPressureDecision(
            volume,
            totalBytes,
            freeBytes,
            freePercent,
            warning,
            critical,
            level,
            reclaim,
            reason,
            Hash(canonical));
    }

    private static string NormalizeIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Volume identity is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe volume identity '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
