using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B950;

public sealed class ReleaseNotesIntegrityPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesOrdersAndFingerprintsReleaseNotes()
    {
        var first = ReleaseNotesIntegrityPolicy.Evaluate(" RC-1 ", "1.2.3-BETA.1+BUILD.5", new[]
        {
            new ReleaseNoteEntry("fix-2", "Fixes", "  Fix   emulator startup  "),
            new ReleaseNoteEntry("feature-1", "Features", "Add deterministic diagnostics")
        });
        var second = ReleaseNotesIntegrityPolicy.Evaluate("rc-1", "1.2.3-beta.1+build.5", new[]
        {
            new ReleaseNoteEntry("feature-1", "features", "Add deterministic diagnostics"),
            new ReleaseNoteEntry("fix-2", "fixes", "Fix emulator startup")
        });

        Assert.Equal("rc-1", first.ReleaseIdentity);
        Assert.Equal("1.2.3-beta.1+build.5", first.Version);
        Assert.Equal(new[] { "feature-1", "fix-2" }, first.Notes.Select(note => note.Identity));
        Assert.Equal("release-notes-valid", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsDuplicateNotesInvalidSemverAndControlCharacters()
    {
        Assert.Throws<ArgumentException>(() => ReleaseNotesIntegrityPolicy.Evaluate("release", "1.0", Array.Empty<ReleaseNoteEntry>()));
        Assert.Throws<ArgumentException>(() => ReleaseNotesIntegrityPolicy.Evaluate("release", "1.0.0", new[]
        {
            new ReleaseNoteEntry("note", "fix", "one"),
            new ReleaseNoteEntry("NOTE", "fix", "two")
        }));
        Assert.Throws<ArgumentException>(() => ReleaseNotesIntegrityPolicy.Evaluate("release", "1.0.0", new[]
        {
            new ReleaseNoteEntry("note", "fix", "bad\nsummary")
        }));
    }

    [Fact]
    public void Evaluate_BoundsNoteCountAndSummaryLength()
    {
        var decision = ReleaseNotesIntegrityPolicy.Evaluate("release", "1.0.0", new[]
        {
            new ReleaseNoteEntry("note", "fix", new string('x', 200))
        }, maxNotes: 1, maxSummaryLength: 40);
        Assert.Equal(40, Assert.Single(decision.Notes).Summary.Length);

        Assert.Throws<ArgumentOutOfRangeException>(() => ReleaseNotesIntegrityPolicy.Evaluate("release", "1.0.0", new[]
        {
            new ReleaseNoteEntry("a", "fix", "a"),
            new ReleaseNoteEntry("b", "fix", "b")
        }, maxNotes: 1));
    }
}
