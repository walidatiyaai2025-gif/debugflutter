using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1650;

public sealed class UiPreferencePolicyTests
{
    [Fact]
    public void Evaluate_NormalizesThemeLanguageAndScale()
    {
        var result = UiPreferenceNormalizationPolicy.Evaluate("profile-a", new[]
        {
            new UiPreference("theme", "DARK"),
            new UiPreference("language", "AR"),
            new UiPreference("text-scale", "3.5", true)
        });

        Assert.Equal("dark", result.Theme);
        Assert.Equal("ar", result.Language);
        Assert.Equal(2.0, result.TextScale);
        Assert.True(result.AccessibilityExplicit);
    }
}
