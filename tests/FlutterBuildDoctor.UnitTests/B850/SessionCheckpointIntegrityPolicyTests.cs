using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B850;

public sealed class SessionCheckpointIntegrityPolicyTests
{
    [Fact]
    public void Resolve_OrdersCheckpointsSelectsLatestAndFingerprintsDeterministically()
    {
        var hashA = new string('A', 64);
        var hashB = new string('B', 64);
        var now = new DateTimeOffset(2026, 8, 11, 20, 0, 0, TimeSpan.Zero);
        var input = new[]
        {
            new SessionCheckpoint(" second ", 2, now, hashB),
            new SessionCheckpoint("first", 1, now.AddMinutes(-1), hashA)
        };

        var first = SessionCheckpointIntegrityPolicy.Resolve(" SESSION-1 ", input);
        var second = SessionCheckpointIntegrityPolicy.Resolve("session-1", input.OrderBy(item => item.Sequence));

        Assert.Equal(new[] { 1, 2 }, first.Checkpoints.Select(item => item.Sequence));
        Assert.Equal(2, first.Latest!.Sequence);
        Assert.Equal(hashB.ToLowerInvariant(), first.Latest.StateFingerprint);
        Assert.Equal("checkpoint-set-valid", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Resolve_RejectsDuplicateSequence()
    {
        var hash = new string('a', 64);
        Assert.Throws<ArgumentException>(() => SessionCheckpointIntegrityPolicy.Resolve("session", new[]
        {
            new SessionCheckpoint("a", 1, DateTimeOffset.UtcNow, hash),
            new SessionCheckpoint("b", 1, DateTimeOffset.UtcNow, hash)
        }));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void NormalizeFingerprint_RejectsInvalidStateFingerprint(string value)
        => Assert.Throws<ArgumentException>(() => SessionCheckpointIntegrityPolicy.NormalizeFingerprint(value));

    [Fact]
    public void Resolve_AllowsEmptyCheckpointSet()
    {
        var result = SessionCheckpointIntegrityPolicy.Resolve("session", Array.Empty<SessionCheckpoint>());
        Assert.Null(result.Latest);
        Assert.Equal("checkpoint-set-empty", result.ReasonCode);
    }
}
