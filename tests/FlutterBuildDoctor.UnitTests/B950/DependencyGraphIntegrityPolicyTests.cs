using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B950;

public sealed class DependencyGraphIntegrityPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesOrdersAndFingerprintsGraph()
    {
        var first = DependencyGraphIntegrityPolicy.Evaluate(new[]
        {
            new DependencyGraphNode(" App ", new[] { "Core", " tools " }),
            new DependencyGraphNode("tools", new[] { "core" }),
            new DependencyGraphNode("CORE")
        });
        var second = DependencyGraphIntegrityPolicy.Evaluate(new[]
        {
            new DependencyGraphNode("core"),
            new DependencyGraphNode("TOOLS", new[] { "CORE" }),
            new DependencyGraphNode("APP", new[] { "TOOLS", "CORE" })
        });

        Assert.Equal(new[] { "app", "core", "tools" }, first.Nodes.Select(node => node.Name));
        Assert.Equal(new[] { "core", "tools", "app" }, first.TopologicalOrder);
        Assert.Equal("dependency-graph-valid", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsDuplicateAndSelfDependencies()
    {
        Assert.Throws<ArgumentException>(() => DependencyGraphIntegrityPolicy.Evaluate(new[]
        {
            new DependencyGraphNode("core"),
            new DependencyGraphNode("CORE")
        }));

        Assert.Throws<ArgumentException>(() => DependencyGraphIntegrityPolicy.Evaluate(new[]
        {
            new DependencyGraphNode("core", new[] { "core" })
        }));
    }

    [Fact]
    public void Evaluate_RejectsUnknownReferencesAndCycles()
    {
        Assert.Throws<ArgumentException>(() => DependencyGraphIntegrityPolicy.Evaluate(new[]
        {
            new DependencyGraphNode("app", new[] { "missing" })
        }));

        Assert.Throws<ArgumentException>(() => DependencyGraphIntegrityPolicy.Evaluate(new[]
        {
            new DependencyGraphNode("a", new[] { "b" }),
            new DependencyGraphNode("b", new[] { "a" })
        }));
    }

    [Fact]
    public void Evaluate_BoundsGraphSizeAndRejectsUnsafeIdentity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DependencyGraphIntegrityPolicy.Evaluate(
            new[] { new DependencyGraphNode("a"), new DependencyGraphNode("b") }, 1));
        Assert.Throws<ArgumentException>(() => DependencyGraphIntegrityPolicy.Evaluate(
            new[] { new DependencyGraphNode("../bad") }));
    }
}
