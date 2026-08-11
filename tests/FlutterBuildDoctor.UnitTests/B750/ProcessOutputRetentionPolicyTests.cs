using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B750;

public sealed class ProcessOutputRetentionPolicyTests
{
    [Fact]
    public void Evaluate_RedactsSecretsPrioritizesStderrAndNormalizesUtc()
    {
        var local = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.FromHours(3));
        var result = ProcessOutputRetentionPolicy.Evaluate(new[]
        {
            new ProcessOutputLine(RetainedOutputStream.Stdout, local, "token=super-secret"),
            new ProcessOutputLine(RetainedOutputStream.Stderr, local.AddMinutes(1), "build failed"),
            new ProcessOutputLine(RetainedOutputStream.Stdout, local.AddMinutes(2), "done")
        });

        Assert.Equal(RetainedOutputStream.Stderr, result.Lines[0].Stream);
        Assert.Contains(result.Lines, line => line.Text == "[REDACTED]");
        Assert.All(result.Lines, line => Assert.Equal(TimeSpan.Zero, line.TimestampUtc.Offset));
        Assert.Equal("output-retained", result.ReasonCode);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Evaluate_ClampsLineLimitAndReportsTruncation()
    {
        var now = DateTimeOffset.UtcNow;
        var lines = Enumerable.Range(0, 5)
            .Select(index => new ProcessOutputLine(RetainedOutputStream.Stdout, now.AddSeconds(index), $"line-{index}"));

        var result = ProcessOutputRetentionPolicy.Evaluate(lines, requestedLineLimit: 2);

        Assert.Equal(2, result.Lines.Count);
        Assert.True(result.Truncated);
        Assert.Equal("output-retained-truncated", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_TruncatesOversizedLineAndFingerprintsDeterministically()
    {
        var now = new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
        var input = new[] { new ProcessOutputLine(RetainedOutputStream.Stdout, now, new string('x', 5_000)) };

        var first = ProcessOutputRetentionPolicy.Evaluate(input);
        var second = ProcessOutputRetentionPolicy.Evaluate(input);

        Assert.Equal(ProcessOutputRetentionPolicy.MaxLineCharacters, first.Lines[0].Text.Length);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsTooManyInputLines()
    {
        var lines = Enumerable.Range(0, ProcessOutputRetentionPolicy.MaxInputLines + 1)
            .Select(index => new ProcessOutputLine(RetainedOutputStream.Stdout, DateTimeOffset.UnixEpoch, index.ToString()));

        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessOutputRetentionPolicy.Evaluate(lines));
    }
}
