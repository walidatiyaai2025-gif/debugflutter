using FlutterBuildDoctor.Application.Diagnostics;

namespace FlutterBuildDoctor.UnitTests.B550;

public sealed class DiagnosticSnapshotDiffPolicyTests
{
    [Fact]
    public void Compare_DetectsAddedRemovedChangedAndIgnoresUnchanged()
    {
        var t1 = new DateTimeOffset(2026, 8, 11, 16, 0, 0, TimeSpan.FromHours(3));
        var before = new DiagnosticSnapshot("snap-1", t1, new Dictionary<string, string>
        {
            ["flutter.version"] = "3.22.0",
            ["java.version"] = "17",
            ["unchanged"] = "same"
        });
        var after = new DiagnosticSnapshot("snap-2", t1.AddMinutes(5), new Dictionary<string, string>
        {
            ["flutter.version"] = "3.24.0",
            ["android.sdk"] = "35",
            ["unchanged"] = "same"
        });

        var result = DiagnosticSnapshotDiffPolicy.Compare(before, after);

        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, item => item.Key == "android.sdk" && item.Kind == DiagnosticDiffKind.Added);
        Assert.Contains(result.Items, item => item.Key == "java.version" && item.Kind == DiagnosticDiffKind.Removed);
        Assert.Contains(result.Items, item => item.Key == "flutter.version" && item.Kind == DiagnosticDiffKind.Changed);
        Assert.DoesNotContain(result.Items, item => item.Key == "unchanged");
        Assert.Equal("added:1;removed:1;changed:1", result.Summary);
        Assert.Equal(TimeSpan.Zero, result.FromCapturedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, result.ToCapturedAtUtc.Offset);
        Assert.Equal(64, result.Fingerprint.Length);
    }

    [Fact]
    public void Compare_IsDeterministicAcrossFactInsertionOrder()
    {
        var t = DateTimeOffset.UtcNow;
        var leftA = new DiagnosticSnapshot("a", t, new Dictionary<string, string> { ["b"] = "1", ["a"] = "1" });
        var leftB = new DiagnosticSnapshot("a", t, new Dictionary<string, string> { ["a"] = "1", ["b"] = "1" });
        var right = new DiagnosticSnapshot("b", t.AddMinutes(1), new Dictionary<string, string> { ["a"] = "2", ["c"] = "3" });

        Assert.Equal(DiagnosticSnapshotDiffPolicy.Compare(leftA, right).Fingerprint, DiagnosticSnapshotDiffPolicy.Compare(leftB, right).Fingerprint);
    }

    [Fact]
    public void Normalize_RejectsInvalidIdentityAndFactValues()
    {
        Assert.Throws<ArgumentException>(() => DiagnosticSnapshotDiffPolicy.Normalize(new DiagnosticSnapshot("bad id", DateTimeOffset.UtcNow, new Dictionary<string, string>())));
        Assert.Throws<ArgumentException>(() => DiagnosticSnapshotDiffPolicy.Normalize(new DiagnosticSnapshot("ok", DateTimeOffset.UtcNow, new Dictionary<string, string> { ["fact"] = "bad\nvalue" })));
    }

    [Fact]
    public void Compare_BoundsDiffItemCount()
    {
        var before = new DiagnosticSnapshot("a", DateTimeOffset.UtcNow, new Dictionary<string, string>());
        var facts = Enumerable.Range(0, DiagnosticSnapshotDiffPolicy.MaxDiffItems + 1).ToDictionary(index => $"fact.{index}", index => index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var after = new DiagnosticSnapshot("b", DateTimeOffset.UtcNow, facts);
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticSnapshotDiffPolicy.Compare(before, after));
    }
}
