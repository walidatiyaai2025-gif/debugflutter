using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B650;

public sealed class DiagnosticSamplingPolicyTests
{
    [Fact]
    public void Apply_DeduplicatesBucketsButPreservesCriticalSamples()
    {
        var start = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        var samples = new[]
        {
            new DiagnosticSample(" Build ", "same", DiagnosticSampleSeverity.Information, start),
            new DiagnosticSample("build", "same", DiagnosticSampleSeverity.Information, start.AddMilliseconds(10)),
            new DiagnosticSample("build", "critical-1", DiagnosticSampleSeverity.Critical, start.AddMilliseconds(20)),
            new DiagnosticSample("build", "critical-2", DiagnosticSampleSeverity.Critical, start.AddMilliseconds(30))
        };

        var result = DiagnosticSamplingPolicy.Apply(samples, 2, TimeSpan.FromSeconds(1));

        Assert.Equal(2, result.MaxSamples);
        Assert.Contains(result.Samples, item => item.Message == "critical-1");
        Assert.Contains(result.Samples, item => item.Message == "critical-2");
        Assert.Equal("samples-downsampled", result.ReasonCode);
        Assert.All(result.Samples, item => Assert.Equal(TimeSpan.Zero, item.ObservedAt.Offset));
    }

    [Fact]
    public void Apply_ClampsBoundsAndIsDeterministic()
    {
        var start = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            new DiagnosticSample("doctor", "a", DiagnosticSampleSeverity.Warning, start),
            new DiagnosticSample("doctor", "b", DiagnosticSampleSeverity.Warning, start.AddSeconds(1))
        };
        var first = DiagnosticSamplingPolicy.Apply(samples, 1000, TimeSpan.FromMilliseconds(1));
        var second = DiagnosticSamplingPolicy.Apply(samples.AsEnumerable().Reverse(), 1000, TimeSpan.FromMilliseconds(1));

        Assert.Equal(DiagnosticSamplingPolicy.MaxRetainedSamples, first.MaxSamples);
        Assert.Equal(DiagnosticSamplingPolicy.MinInterval, first.Interval);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Theory]
    [InlineData("bad category")]
    [InlineData("../doctor")]
    public void NormalizeCategory_RejectsUnsafeValues(string value)
        => Assert.Throws<ArgumentException>(() => DiagnosticSamplingPolicy.NormalizeCategory(value));

    [Fact]
    public void Normalize_RejectsControlCharactersInMessage()
        => Assert.Throws<ArgumentException>(() => DiagnosticSamplingPolicy.Normalize(
            new DiagnosticSample("doctor", "bad\nmessage", DiagnosticSampleSeverity.Information, DateTimeOffset.UtcNow)));
}
