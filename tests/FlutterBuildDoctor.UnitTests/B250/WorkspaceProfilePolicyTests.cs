using FlutterBuildDoctor.Application.Workspaces;

namespace FlutterBuildDoctor.UnitTests.B250;

public sealed class WorkspaceProfilePolicyTests
{
    [Fact]
    public void Resolve_MergesDefaultsAndRepositoryOverridesIntoImmutableSnapshot()
    {
        var input = new WorkspaceProfileInput(
            "  Main   Project ",
            PreferredJdk: null,
            PreferredDevice: "emulator-5554",
            PreferredBuildProfile: null,
            LastUsedAt: new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(3)));
        var defaults = new WorkspaceProfileDefaults("embedded", "device-default", "debug");
        var repositoryOverride = new WorkspaceProfileOverride(@"C:\Java\jdk-21", null, "release");

        var resolved = WorkspaceProfilePolicy.Resolve(input, defaults, repositoryOverride);

        Assert.Equal("Main Project", resolved.Name);
        Assert.Equal(@"C:\Java\jdk-21", resolved.PreferredJdk);
        Assert.Equal("emulator-5554", resolved.PreferredDevice);
        Assert.Equal("release", resolved.PreferredBuildProfile);
        Assert.Equal(TimeSpan.Zero, resolved.LastUsedAtUtc.Offset);
        Assert.Equal(100, resolved.CompletenessScore);
        Assert.Equal(64, resolved.Fingerprint.Length);
    }

    [Fact]
    public void Resolve_IsDeterministicForEquivalentInputs()
    {
        var input = new WorkspaceProfileInput("A", "embedded", " pixel ", "PROFILE", DateTimeOffset.UnixEpoch);
        var defaults = new WorkspaceProfileDefaults();

        var first = WorkspaceProfilePolicy.Resolve(input, defaults);
        var second = WorkspaceProfilePolicy.Resolve(input, defaults);

        Assert.Equal("embedded", first.PreferredJdk);
        Assert.Equal("pixel", first.PreferredDevice);
        Assert.Equal("profile", first.PreferredBuildProfile);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Theory]
    [InlineData("embedded", "embedded")]
    [InlineData("ANDROID-STUDIO", "android-studio")]
    public void ValidateJdk_AcceptsSupportedTokens(string input, string expected)
    {
        Assert.Equal(expected, WorkspaceProfilePolicy.ValidateJdk(input));
    }

    [Fact]
    public void Validation_RejectsBadNamesRelativeJdkAndUnknownBuildProfile()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceProfilePolicy.NormalizeName("   "));
        Assert.Throws<ArgumentException>(() => WorkspaceProfilePolicy.ValidateJdk("relative/jdk"));
        Assert.Throws<ArgumentException>(() => WorkspaceProfilePolicy.NormalizeBuildProfile("benchmark"));
    }

    [Fact]
    public void Resolve_CompletenessReflectsMissingOptionalPreferences()
    {
        var resolved = WorkspaceProfilePolicy.Resolve(
            new WorkspaceProfileInput("Minimal", null, null, null, DateTimeOffset.UtcNow),
            new WorkspaceProfileDefaults());

        Assert.Null(resolved.PreferredJdk);
        Assert.Null(resolved.PreferredDevice);
        Assert.Equal("debug", resolved.PreferredBuildProfile);
        Assert.Equal(40, resolved.CompletenessScore);
    }
}
