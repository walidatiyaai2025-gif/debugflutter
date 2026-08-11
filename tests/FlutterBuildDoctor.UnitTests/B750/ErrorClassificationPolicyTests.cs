using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B750;

public sealed class ErrorClassificationPolicyTests
{
    [Fact]
    public void Evaluate_ClassifiesNetworkTimeoutAsRetryableError()
    {
        var result = ErrorClassificationPolicy.Evaluate(new ErrorClassificationRequest(
            " NET-001 ",
            "Connection timeout while downloading SDK",
            new[] { "mirror unavailable", "retry later" }));

        Assert.Equal("net-001", result.Code);
        Assert.Equal(ClassifiedErrorCategory.Network, result.Category);
        Assert.Equal(ClassifiedErrorSeverity.Error, result.Severity);
        Assert.True(result.Retryable);
        Assert.False(result.RequiresUserAction);
        Assert.Equal("network:net-001", result.GroupKey);
        Assert.Equal("error-classified", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_ClassifiesFilesystemPermissionAsUserActionRequired()
    {
        var result = ErrorClassificationPolicy.Evaluate(new ErrorClassificationRequest(
            "fs-403",
            "Access denied: permission required for directory"));

        Assert.Equal(ClassifiedErrorCategory.Filesystem, result.Category);
        Assert.True(result.RequiresUserAction);
        Assert.False(result.Retryable);
    }

    [Fact]
    public void Evaluate_BoundsEvidenceAndFingerprintsDeterministically()
    {
        var evidence = Enumerable.Range(0, 50).Select(index => $"evidence-{index:D2}").ToArray();
        var request = new ErrorClassificationRequest("build-1", "compile failed", evidence);

        var first = ErrorClassificationPolicy.Evaluate(request);
        var second = ErrorClassificationPolicy.Evaluate(request);

        Assert.Equal(ErrorClassificationPolicy.MaxEvidenceRecords, first.Evidence.Count);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad code")]
    [InlineData("../error")]
    public void NormalizeCode_RejectsUnsafeValues(string value)
        => Assert.ThrowsAny<ArgumentException>(() => ErrorClassificationPolicy.NormalizeCode(value));
}
