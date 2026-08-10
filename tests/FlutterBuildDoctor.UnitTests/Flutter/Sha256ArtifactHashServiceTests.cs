using FlutterBuildDoctor.Flutter.Build;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class Sha256ArtifactHashServiceTests
{
    [Fact]
    public async Task ComputeSha256Async_ReturnsStableLowercaseDigest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fbd-hash-{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllTextAsync(path, "abc");
            var service = new Sha256ArtifactHashService();

            var hash = await service.ComputeSha256Async(path);

            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
