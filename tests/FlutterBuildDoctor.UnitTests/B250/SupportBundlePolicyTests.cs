using FlutterBuildDoctor.Application.Support;

namespace FlutterBuildDoctor.UnitTests.B250;

public sealed class SupportBundlePolicyTests
{
    [Fact]
    public void Build_SanitizesOrdersIncludesSummariesAndFingerprintsDeterministically()
    {
        var items = new[]
        {
            new SupportBundleItem(" z log.txt ", "ok", SupportBundleCategory.Logs),
            new SupportBundleItem("a.txt", "token=abc123", SupportBundleCategory.General)
        };

        var first = SupportBundlePolicy.Build(
            items,
            environmentSummary: "password:supersecret",
            problemSummary: "Bearer abc.def.ghi");
        var second = SupportBundlePolicy.Build(
            items.AsEnumerable().Reverse(),
            environmentSummary: "password:supersecret",
            problemSummary: "Bearer abc.def.ghi");

        Assert.Equal(4, first.Items.Count);
        Assert.Equal("environment-summary.txt", first.Items[0].Name);
        Assert.Contains("[REDACTED]", first.Items[0].Content, StringComparison.Ordinal);
        Assert.Equal("problem-summary.txt", first.Items[1].Name);
        Assert.Contains("Bearer [REDACTED]", first.Items[1].Content, StringComparison.Ordinal);
        Assert.Equal("z_log.txt", first.Items[2].Name);
        Assert.Equal("a.txt", first.Items[3].Name);
        Assert.Equal(64, first.Fingerprint.Length);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Theory]
    [InlineData("password=hunter2")]
    [InlineData("api_key: value")]
    [InlineData("secret = value")]
    [InlineData("Authorization Bearer abcdef.12345")]
    public void RedactText_RemovesKnownSecretsAndTokenPatterns(string input)
    {
        var redacted = SupportBundlePolicy.RedactText(input);
        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(" value", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdef.12345", redacted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactText_BoundsLineLength()
    {
        var redacted = SupportBundlePolicy.RedactText(new string('a', 100), maxLineLength: 40);
        Assert.Equal(40, redacted.Length);
        Assert.EndsWith("...", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsBinaryAndOversizedBundles()
    {
        Assert.Throws<ArgumentException>(() => SupportBundlePolicy.Build(new[]
        {
            new SupportBundleItem("binary.bin", "abc", IsBinary: true)
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => SupportBundlePolicy.Build(
            Enumerable.Range(0, SupportBundlePolicy.MaxItems + 1)
                .Select(index => new SupportBundleItem($"item-{index}.txt", "ok"))));
    }

    [Fact]
    public void SanitizeName_RejectsUnsafeNames()
    {
        Assert.Equal("hello_world.txt", SupportBundlePolicy.SanitizeName(" hello world.txt "));
        Assert.Throws<ArgumentException>(() => SupportBundlePolicy.SanitizeName("../secrets.txt"));
    }
}
