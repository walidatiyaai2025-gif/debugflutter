using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record ExecutionPhaseTransitionDecision(string SessionIdentity, string CurrentPhase, string NextPhase, long CurrentSequence, long NextSequence, bool Allowed, bool Terminal, string ReasonCode, string Fingerprint);

public static class ExecutionPhaseTransitionPolicy
{
    private static readonly HashSet<string> Phases = new(StringComparer.Ordinal) { "queued", "preparing", "running", "verifying", "completed", "failed", "cancelled" };
    private static readonly HashSet<string> Terminal = new(StringComparer.Ordinal) { "completed", "failed", "cancelled" };

    public static ExecutionPhaseTransitionDecision Evaluate(string sessionIdentity, string currentPhase, string nextPhase, long currentSequence, long nextSequence)
    {
        var session = B1550PolicyHelpers.Identity(sessionIdentity, nameof(sessionIdentity));
        var current = NormalizePhase(currentPhase, nameof(currentPhase));
        var next = NormalizePhase(nextPhase, nameof(nextPhase));
        if (currentSequence < 0 || nextSequence < 0) throw new ArgumentOutOfRangeException(nameof(currentSequence));
        var monotonic = nextSequence > currentSequence;
        var terminalCurrent = Terminal.Contains(current);
        var normalNext = current switch
        {
            "queued" => "preparing",
            "preparing" => "running",
            "running" => "verifying",
            "verifying" => "completed",
            _ => null
        };
        var allowedTarget = next == normalNext || (!terminalCurrent && next is "failed" or "cancelled");
        var allowed = monotonic && !terminalCurrent && allowedTarget;
        var reason = !monotonic ? "phase-transition-sequence-regression" : terminalCurrent ? "phase-transition-terminal" : allowed ? "phase-transition-allowed" : "phase-transition-invalid";
        var payload = $"{session}|{current}|{next}|{currentSequence}|{nextSequence}|{allowed}|{reason}";
        return new ExecutionPhaseTransitionDecision(session, current, next, currentSequence, nextSequence, allowed, Terminal.Contains(next), reason, B1550PolicyHelpers.Fingerprint(payload));
    }

    private static string NormalizePhase(string value, string paramName)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!Phases.Contains(normalized)) throw new ArgumentException($"Unsupported execution phase '{value}'.", paramName);
        return normalized;
    }
}

public sealed record ReleaseMetadataRecord(string ReleaseIdentity, string Channel, long BuildNumber, string CommitFingerprint, DateTimeOffset CreatedAtUtc);
public sealed record ReleaseMetadataContinuityDecision(ReleaseMetadataRecord Current, ReleaseMetadataRecord? Previous, bool Continuous, IReadOnlyList<string> Findings, string ReasonCode, string Fingerprint);

public static class ReleaseMetadataContinuityPolicy
{
    private static readonly HashSet<string> Channels = new(StringComparer.Ordinal) { "dev", "beta", "stable" };
    private static readonly Regex CommitPattern = new("^[a-f0-9]{40,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ReleaseMetadataContinuityDecision Evaluate(ReleaseMetadataRecord current, ReleaseMetadataRecord? previous = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        var normalizedCurrent = Normalize(current);
        var normalizedPrevious = previous is null ? null : Normalize(previous);
        var findings = new List<string>();
        if (normalizedPrevious is not null)
        {
            if (normalizedCurrent.Channel != normalizedPrevious.Channel) findings.Add("channel-changed");
            if (normalizedCurrent.BuildNumber <= normalizedPrevious.BuildNumber) findings.Add("build-not-increasing");
            if (normalizedCurrent.CreatedAtUtc < normalizedPrevious.CreatedAtUtc) findings.Add("timestamp-regressed");
        }
        findings.Sort(StringComparer.Ordinal);
        var continuous = findings.Count == 0;
        var reason = continuous ? "release-metadata-continuous" : "release-metadata-discontinuous";
        var payload = $"{normalizedCurrent}|{normalizedPrevious}|{string.Join(',', findings)}|{continuous}";
        return new ReleaseMetadataContinuityDecision(normalizedCurrent, normalizedPrevious, continuous, findings, reason, B1550PolicyHelpers.Fingerprint(payload));
    }

    private static ReleaseMetadataRecord Normalize(ReleaseMetadataRecord record)
    {
        var release = B1550PolicyHelpers.Identity(record.ReleaseIdentity, nameof(record.ReleaseIdentity));
        var channel = (record.Channel ?? string.Empty).Trim().ToLowerInvariant();
        if (!Channels.Contains(channel)) throw new ArgumentException($"Unsupported release channel '{record.Channel}'.", nameof(record.Channel));
        if (record.BuildNumber < 0) throw new ArgumentOutOfRangeException(nameof(record.BuildNumber));
        var commit = (record.CommitFingerprint ?? string.Empty).Trim().ToLowerInvariant();
        if (!CommitPattern.IsMatch(commit)) throw new ArgumentException("Commit fingerprint must be 40-64 hexadecimal characters.", nameof(record.CommitFingerprint));
        return new ReleaseMetadataRecord(release, channel, record.BuildNumber, commit, B1550PolicyHelpers.Utc(record.CreatedAtUtc));
    }
}
