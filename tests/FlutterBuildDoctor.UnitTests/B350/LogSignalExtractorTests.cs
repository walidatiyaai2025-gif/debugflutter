using FlutterBuildDoctor.Application.Logging;

namespace FlutterBuildDoctor.UnitTests.B350;

public sealed class LogSignalExtractorTests
{
    [Fact]
    public void Extract_NormalizesLineEndingsBoundsLinesAndFingerprintsDeterministically()
    {
        const string log = "warning: first\r\nerror: second\rinfo\nwarning: ignored";

        var first = LogSignalExtractor.Extract(log, maxLines: 3);
        var second = LogSignalExtractor.Extract(LogSignalExtractor.NormalizeLineEndings(log), maxLines: 3);

        Assert.Equal(3, first.RetainedLineCount);
        Assert.Equal(2, first.Signals.Count);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Extract_DetectsGradleTaskFailureCode()
    {
        var result = LogSignalExtractor.Extract("Execution failed for task ':app:compileReleaseKotlin'.");
        var signal = Assert.Single(result.Signals);

        Assert.Equal("gradle-task-failure", signal.Key);
        Assert.Equal(":app:compileReleaseKotlin", signal.Code);
        Assert.Equal(LogSignalSeverity.Error, signal.Severity);
    }

    [Fact]
    public void Extract_DetectsFlutterAnalyzerCode()
    {
        var result = LogSignalExtractor.Extract("error - The method is missing - lib/main.dart:10:2 - undefined_method");
        var signal = Assert.Single(result.Signals);

        Assert.Equal("flutter-analyzer", signal.Key);
        Assert.Equal("undefined_method", signal.Code);
        Assert.Equal(LogSignalSeverity.Error, signal.Severity);
    }

    [Fact]
    public void Extract_CollapsesDuplicateSignalsAndCountsOccurrences()
    {
        const string log = "warning: package old\nWARNING: package old\nerror: failed build\nerror: failed build";
        var result = LogSignalExtractor.Extract(log);

        Assert.Equal(2, result.Signals.Count);
        Assert.Equal(2, result.Signals.Single(signal => signal.Key == "generic-error").Occurrences);
        Assert.Equal(2, result.Signals.Single(signal => signal.Key == "generic-warning").Occurrences);
    }

    [Fact]
    public void Extract_OrdersErrorsBeforeWarningsThenByStableKey()
    {
        var result = LogSignalExtractor.Extract("warning: z\nerror: b\nExecution failed for task ':app:a'.");

        Assert.Equal(LogSignalSeverity.Error, result.Signals[0].Severity);
        Assert.Equal(LogSignalSeverity.Error, result.Signals[1].Severity);
        Assert.Equal(LogSignalSeverity.Warning, result.Signals[2].Severity);
        Assert.Equal("generic-error", result.Signals[0].Key);
        Assert.Equal("gradle-task-failure", result.Signals[1].Key);
    }

    [Theory]
    [InlineData("ERROR something", LogSignalSeverity.Error)]
    [InlineData("build FAILED", LogSignalSeverity.Error)]
    [InlineData("warning: deprecated", LogSignalSeverity.Warning)]
    [InlineData("all good", LogSignalSeverity.Info)]
    public void ClassifySeverity_IsCaseInsensitive(string line, LogSignalSeverity expected)
    {
        Assert.Equal(expected, LogSignalExtractor.ClassifySeverity(line));
    }
}
