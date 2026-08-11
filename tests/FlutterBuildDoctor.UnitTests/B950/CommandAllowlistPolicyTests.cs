using System;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B950;

public sealed class CommandAllowlistPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesAllowsAndFingerprintsApprovedCommand()
    {
        var first = CommandAllowlistPolicy.Evaluate(
            " Git-Status ", "GIT.EXE", new[] { "status", "--porcelain=v1" },
            new[] { "flutter", "git" }, new[] { "status", "--porcelain" });
        var second = CommandAllowlistPolicy.Evaluate(
            "git-status", "git", new[] { "status", "--porcelain=v1" },
            new[] { "git" }, new[] { "--porcelain", "status" });

        Assert.True(first.Allowed);
        Assert.Equal("git", first.Executable);
        Assert.Equal("git status --porcelain=v1", first.SafeSummary);
        Assert.Equal("command-allowlisted", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ReturnsDeniedForExecutableOrArgumentOutsideAllowlist()
    {
        var executableDenied = CommandAllowlistPolicy.Evaluate("cmd", "powershell", Array.Empty<string>(), new[] { "git" });
        var argumentDenied = CommandAllowlistPolicy.Evaluate("cmd", "git", new[] { "push" }, new[] { "git" }, new[] { "status" });

        Assert.False(executableDenied.Allowed);
        Assert.Equal("command-executable-not-allowlisted", executableDenied.ReasonCode);
        Assert.False(argumentDenied.Allowed);
        Assert.Equal("command-argument-not-allowlisted", argumentDenied.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsTraversalShellOperatorsAndInlineSecrets()
    {
        Assert.Throws<ArgumentException>(() => CommandAllowlistPolicy.Evaluate("cmd", "..\\git.exe", null, new[] { "git" }));
        Assert.Throws<ArgumentException>(() => CommandAllowlistPolicy.Evaluate("cmd", "git", new[] { "status && whoami" }, new[] { "git" }));
        Assert.Throws<ArgumentException>(() => CommandAllowlistPolicy.Evaluate("cmd", "git", new[] { "--token=abc123" }, new[] { "git" }));
    }
}
