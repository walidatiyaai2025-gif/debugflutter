using System;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1550;

public sealed class AtomicFileWriteSafetyPolicyTests
{
    private static readonly string HashA = new('a', 64);

    [Fact]
    public void Evaluate_NormalizesAndRequiresSameVolume()
    {
        var eligible = AtomicFileWriteSafetyPolicy.Evaluate(" Write.Op ", "build\\app.apk", HashA.ToUpperInvariant(), "VOL-A", "vol-a");
        var cross = AtomicFileWriteSafetyPolicy.Evaluate("write.op", "build/app.apk", HashA, "vol-a", "vol-b");
        Assert.Equal("write.op", eligible.OperationIdentity);
        Assert.Equal("build/app.apk", eligible.TargetPath);
        Assert.Equal($"build/app.apk.tmp.{HashA[..12]}", eligible.TemporaryPath);
        Assert.True(eligible.AtomicReplaceEligible);
        Assert.False(cross.AtomicReplaceEligible);
        Assert.Equal("atomic-write-cross-volume", cross.ReasonCode);
        Assert.Equal(64, eligible.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsUnsafePathAndHash()
    {
        Assert.Throws<ArgumentException>(() => AtomicFileWriteSafetyPolicy.Evaluate("op", "/tmp/a", HashA, "vol", "vol"));
        Assert.Throws<ArgumentException>(() => AtomicFileWriteSafetyPolicy.Evaluate("op", "a/../b", HashA, "vol", "vol"));
        Assert.Throws<ArgumentException>(() => AtomicFileWriteSafetyPolicy.Evaluate("op", "a/b", "bad", "vol", "vol"));
    }
}
