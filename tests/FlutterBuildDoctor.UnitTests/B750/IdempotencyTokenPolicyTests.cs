using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B750;

public sealed class IdempotencyTokenPolicyTests
{
    [Fact]
    public void Evaluate_CreatesNormalizedBoundedTokenDecision()
    {
        var issued = new DateTimeOffset(2026, 8, 11, 20, 0, 0, TimeSpan.FromHours(3));
        var payload = new string('A', 64);

        var result = IdempotencyTokenPolicy.Evaluate(" Build ", " Request-001 ", issued, TimeSpan.FromDays(3), payload, issued);

        Assert.True(result.Allowed);
        Assert.False(result.IsReplay);
        Assert.Equal("build", result.Operation);
        Assert.Equal("request-001", result.Token);
        Assert.Equal(new string('a', 64), result.PayloadFingerprint);
        Assert.Equal(IdempotencyTokenPolicy.MaxLifetime, result.ExpiresAtUtc - result.IssuedAtUtc);
        Assert.Equal(TimeSpan.Zero, result.IssuedAtUtc.Offset);
        Assert.Equal("idempotency-token-created", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_DetectsSafeReplayAndPreservesOriginalResultFingerprint()
    {
        var issued = DateTimeOffset.UtcNow;
        var payload = new string('a', 64);
        var resultHash = new string('b', 64);
        var existing = new IdempotencyRecord("build", "request-001", issued, issued.AddHours(1), payload, resultHash);

        var result = IdempotencyTokenPolicy.Evaluate("build", "request-001", issued, TimeSpan.FromHours(1), payload, issued.AddMinutes(5), existing);

        Assert.True(result.Allowed);
        Assert.True(result.IsReplay);
        Assert.Equal(resultHash, result.ResultFingerprint);
        Assert.Equal("idempotency-safe-replay", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsPayloadConflict()
    {
        var issued = DateTimeOffset.UtcNow;
        var existing = new IdempotencyRecord("build", "request-001", issued, issued.AddHours(1), new string('a', 64), new string('c', 64));

        var result = IdempotencyTokenPolicy.Evaluate("build", "request-001", issued, TimeSpan.FromHours(1), new string('b', 64), issued.AddMinutes(1), existing);

        Assert.False(result.Allowed);
        Assert.Equal("idempotency-payload-conflict", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_DetectsExpiredExistingToken()
    {
        var issued = DateTimeOffset.UtcNow.AddHours(-2);
        var existing = new IdempotencyRecord("build", "request-001", issued, issued.AddMinutes(30), new string('a', 64), null);

        var result = IdempotencyTokenPolicy.Evaluate("build", "request-001", issued, TimeSpan.FromMinutes(30), new string('a', 64), DateTimeOffset.UtcNow, existing);

        Assert.False(result.Allowed);
        Assert.True(result.IsExpired);
        Assert.Equal("idempotency-token-expired", result.ReasonCode);
    }
}
