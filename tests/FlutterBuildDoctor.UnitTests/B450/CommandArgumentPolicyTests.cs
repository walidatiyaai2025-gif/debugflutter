using FlutterBuildDoctor.Application.Commands;

namespace FlutterBuildDoctor.UnitTests.B450;

public sealed class CommandArgumentPolicyTests
{
    [Fact]
    public void Prepare_PreservesTypedArgumentsAndRedactsSecrets()
    {
        var decision = CommandArgumentPolicy.Prepare(" flutter ", new[]
        {
            "build", "apk", "--flavor", "demo app", "--token=abc123", "--password", "hunter2"
        });

        Assert.Equal("flutter", decision.Executable);
        Assert.Equal("demo app", decision.Arguments[3]);
        Assert.Equal("--token=[REDACTED]", decision.SafeDisplayArguments[4]);
        Assert.Equal("[REDACTED]", decision.SafeDisplayArguments[6]);
        Assert.True(decision.ContainsSecrets);
        Assert.DoesNotContain("abc123", decision.SafeDisplayCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", decision.SafeDisplayCommand, StringComparison.Ordinal);
        Assert.Contains("\"demo app\"", decision.SafeDisplayCommand, StringComparison.Ordinal);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Prepare_IsDeterministicWithoutReorderingArguments()
    {
        var first = CommandArgumentPolicy.Prepare("flutter", new[] { "build", "apk", "--release" });
        var second = CommandArgumentPolicy.Prepare("flutter", new[] { "build", "apk", "--release" });

        Assert.Equal(new[] { "build", "apk", "--release" }, first.Arguments);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Prepare_RejectsControlCharactersAndBounds()
    {
        Assert.Throws<ArgumentException>(() => CommandArgumentPolicy.Prepare("flutter\n", Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => CommandArgumentPolicy.Prepare("flutter", new[] { "bad\narg" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommandArgumentPolicy.Prepare("flutter", new[] { new string('x', CommandArgumentPolicy.MaxArgumentLength + 1) }));

        var tooMany = Enumerable.Range(0, CommandArgumentPolicy.MaxArguments + 1).Select(index => index.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => CommandArgumentPolicy.Prepare("flutter", tooMany));
    }
}
