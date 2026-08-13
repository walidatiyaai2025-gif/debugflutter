using System;
using System.Collections.Generic;
using System.Linq;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record UiPreference(string Key, string Value, bool AccessibilityExplicit = false);
public sealed record UiPreferenceDecision(
    string ProfileIdentity,
    IReadOnlyList<UiPreference> Preferences,
    string Theme,
    string Language,
    double TextScale,
    bool AccessibilityExplicit,
    string ReasonCode,
    string Fingerprint);

public static class UiPreferenceNormalizationPolicy
{
    public static UiPreferenceDecision Evaluate(string profileIdentity, IEnumerable<UiPreference> preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var profile = B1550PolicyHelpers.Identity(profileIdentity, nameof(profileIdentity));
        var normalized = preferences.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            var key = B1550PolicyHelpers.Identity(item.Key, nameof(item.Key));
            return new UiPreference(key, (item.Value ?? string.Empty).Trim(), item.AccessibilityExplicit);
        }).OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();

        if (normalized.GroupBy(item => item.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Duplicate preference keys are not allowed.", nameof(preferences));

        string Read(string key, string fallback) => normalized.FirstOrDefault(item => item.Key == key)?.Value ?? fallback;
        var themeRaw = Read("theme", "system").ToLowerInvariant();
        var theme = themeRaw is "light" or "dark" or "system" ? themeRaw : "system";
        var languageRaw = Read("language", "en").ToLowerInvariant();
        var language = languageRaw is "ar" or "en" ? languageRaw : "en";
        var scaleRaw = Read("text-scale", "1.0");
        var parsedScale = double.TryParse(scaleRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale) ? scale : 1.0;
        var textScale = Math.Clamp(parsedScale, 0.8, 2.0);
        var accessibilityExplicit = normalized.Any(item => item.AccessibilityExplicit);
        var reason = "ui-preferences-normalized";
        var payload = $"{profile}|{theme}|{language}|{textScale:F2}|{accessibilityExplicit}|{string.Join(';', normalized.Select(item => $"{item.Key}:{item.Value}:{item.AccessibilityExplicit}"))}";
        return new UiPreferenceDecision(profile, normalized, theme, language, textScale, accessibilityExplicit, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}
