using System;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1550;

public sealed class CacheKeyCompatibilityPolicyTests
{
    private static readonly string HashA = new('a', 64);

    [Fact]
    public void Evaluate_NormalizesCanonicalKeyAndDetectsCompatibility()
    {
        var existing = new CacheKeyDescriptor("doctor", "windows", "8.0.0", HashA, "default");
        var same = CacheKeyCompatibilityPolicy.Evaluate("Doctor", "WINDOWS", "8.0", HashA.ToUpperInvariant(), null, existing);
        var cross = CacheKeyCompatibilityPolicy.Evaluate("doctor", "linux", "8.0.0", HashA, "default", existing);
        Assert.Equal("doctor", same.Requested.Namespace);
        Assert.Equal("windows", same.Requested.Platform);
        Assert.Equal("8.0.0", same.Requested.ToolchainVersion);
        Assert.Contains(HashA[..16], same.CanonicalKey, StringComparison.Ordinal);
        Assert.True(same.CompatibleWithExisting);
        Assert.False(same.CrossPlatformMismatch);
        Assert.False(cross.CompatibleWithExisting);
        Assert.True(cross.CrossPlatformMismatch);
        Assert.Equal("cache-key-platform-mismatch", cross.ReasonCode);
        Assert.Equal(64, same.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsInvalidPlatformVersionAndHash()
    {
        Assert.Throws<ArgumentException>(() => CacheKeyCompatibilityPolicy.Evaluate("cache", "solaris", "8.0", HashA, null));
        Assert.Throws<ArgumentException>(() => CacheKeyCompatibilityPolicy.Evaluate("cache", "windows", "bad", HashA, null));
        Assert.Throws<ArgumentException>(() => CacheKeyCompatibilityPolicy.Evaluate("cache", "windows", "8.0", "bad", null));
    }
}
