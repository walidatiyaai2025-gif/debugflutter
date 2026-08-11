using System;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B950;

public sealed class SessionLineagePolicyTests
{
    [Fact]
    public void Evaluate_NormalizesRootsDepthsAndFingerprint()
    {
        var first = SessionLineagePolicy.Evaluate(new[]
        {
            new SessionLineageEntry(" Child-2 ", "child-1"),
            new SessionLineageEntry("ROOT"),
            new SessionLineageEntry("child-1", "root")
        });
        var second = SessionLineagePolicy.Evaluate(new[]
        {
            new SessionLineageEntry("root"),
            new SessionLineageEntry("CHILD-1", "ROOT"),
            new SessionLineageEntry("child-2", "CHILD-1")
        });

        Assert.Equal("root", first.RootBySession["child-2"]);
        Assert.Equal(2, first.DepthBySession["child-2"]);
        Assert.Equal(2, first.MaxDepth);
        Assert.Equal("session-lineage-valid", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsSelfUnknownDuplicateAndCycles()
    {
        Assert.Throws<ArgumentException>(() => SessionLineagePolicy.Evaluate(new[]
        {
            new SessionLineageEntry("a", "a")
        }));
        Assert.Throws<ArgumentException>(() => SessionLineagePolicy.Evaluate(new[]
        {
            new SessionLineageEntry("a", "missing")
        }));
        Assert.Throws<ArgumentException>(() => SessionLineagePolicy.Evaluate(new[]
        {
            new SessionLineageEntry("a"),
            new SessionLineageEntry("A")
        }));
        Assert.Throws<ArgumentException>(() => SessionLineagePolicy.Evaluate(new[]
        {
            new SessionLineageEntry("a", "b"),
            new SessionLineageEntry("b", "a")
        }));
    }

    [Fact]
    public void Evaluate_EnforcesBoundedLineageDepth()
        => Assert.Throws<ArgumentOutOfRangeException>(() => SessionLineagePolicy.Evaluate(new[]
        {
            new SessionLineageEntry("a"),
            new SessionLineageEntry("b", "a"),
            new SessionLineageEntry("c", "b")
        }, maxDepth: 1));
}
