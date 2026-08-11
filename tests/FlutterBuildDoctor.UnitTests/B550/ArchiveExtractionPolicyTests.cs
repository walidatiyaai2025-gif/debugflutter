using FlutterBuildDoctor.Application.Archives;

namespace FlutterBuildDoctor.UnitTests.B550;

public sealed class ArchiveExtractionPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesOrdersAndFingerprintsDeterministically()
    {
        var entries = new[]
        {
            new ArchiveEntryCandidate("lib\\main.dart", 120),
            new ArchiveEntryCandidate("android/app.apk", 300)
        };

        var first = ArchiveExtractionPolicy.Evaluate(entries);
        var second = ArchiveExtractionPolicy.Evaluate(entries.AsEnumerable().Reverse());

        Assert.True(first.Allowed);
        Assert.Equal("extraction-approved", first.ReasonCode);
        Assert.Equal(420, first.TotalBytes);
        Assert.Equal("android/app.apk", first.Entries[0].EntryPath);
        Assert.Equal("lib/main.dart", first.Entries[1].EntryPath);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("folder/../evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("/absolute/file.txt")]
    public void NormalizeEntryPath_RejectsUnsafePaths(string value)
        => Assert.Throws<ArgumentException>(() => ArchiveExtractionPolicy.NormalizeEntryPath(value));

    [Fact]
    public void Evaluate_RejectsLinksAndDuplicateDestinations()
    {
        Assert.Throws<ArgumentException>(() => ArchiveExtractionPolicy.Evaluate(new[]
        {
            new ArchiveEntryCandidate("link", 0, true)
        }));

        Assert.Throws<ArgumentException>(() => ArchiveExtractionPolicy.Evaluate(new[]
        {
            new ArchiveEntryCandidate("lib/main.dart", 1),
            new ArchiveEntryCandidate("LIB\\MAIN.DART", 1)
        }));
    }

    [Fact]
    public void Evaluate_RejectsFileCountAndExpandedSizeBombs()
    {
        var tooMany = Enumerable.Range(0, ArchiveExtractionPolicy.MaxFiles + 1)
            .Select(index => new ArchiveEntryCandidate($"file-{index}.txt", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArchiveExtractionPolicy.Evaluate(tooMany));

        Assert.Throws<ArgumentOutOfRangeException>(() => ArchiveExtractionPolicy.Evaluate(new[]
        {
            new ArchiveEntryCandidate("huge.bin", ArchiveExtractionPolicy.MaxTotalBytes + 1)
        }));
    }
}
