using System;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record ResultSummaryPresentationDecision(string SummaryIdentity, int PassedCount, int FailedCount, int SkippedCount, int TotalCount, double SuccessPercent, string Status, string ReasonCode, string Fingerprint);

public static class ResultSummaryPresentationPolicy
{
    public static ResultSummaryPresentationDecision Evaluate(string summaryIdentity, int passedCount, int failedCount, int skippedCount)
    {
        var identity = B1550PolicyHelpers.Identity(summaryIdentity, nameof(summaryIdentity));
        if (passedCount < 0) throw new ArgumentOutOfRangeException(nameof(passedCount));
        if (failedCount < 0) throw new ArgumentOutOfRangeException(nameof(failedCount));
        if (skippedCount < 0) throw new ArgumentOutOfRangeException(nameof(skippedCount));
        var total = checked(passedCount + failedCount + skippedCount);
        var successPercent = total == 0 ? 0d : Math.Round((double)passedCount / total * 100d, 2, MidpointRounding.AwayFromZero);
        var status = failedCount > 0 ? "failed" : passedCount > 0 ? "passed" : skippedCount > 0 ? "skipped" : "empty";
        var reason = $"result-summary-{status}";
        var payload = $"{identity}|{passedCount}|{failedCount}|{skippedCount}|{total}|{successPercent:F2}|{status}";
        return new ResultSummaryPresentationDecision(identity, passedCount, failedCount, skippedCount, total, successPercent, status, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}
