using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Release;

namespace FlutterBuildDoctor.UnitTests.Release;

public sealed class ReleaseOutputActionServiceTests
{
    [Fact]
    public void OutputActions_UseExplorerWithTypedPaths()
    {
        var artifact = Path.Combine(Path.GetTempPath(), $"fbd-release-{Guid.NewGuid():N}.apk");
        File.WriteAllText(artifact, "apk");
        try
        {
            var launcher = new RecordingLauncher();
            var service = new ReleaseOutputActionService(launcher);

            service.OpenOutputDirectory(artifact);
            Assert.Equal("explorer.exe", launcher.LastRequest!.FileName);
            Assert.Equal(new[] { Path.GetDirectoryName(Path.GetFullPath(artifact))! }, launcher.LastRequest.Arguments);

            service.RevealArtifact(artifact);
            Assert.Equal(new[] { "/select,", Path.GetFullPath(artifact) }, launcher.LastRequest!.Arguments);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    private sealed class RecordingLauncher : IDetachedProcessLauncher
    {
        public ProcessRequest? LastRequest { get; private set; }

        public ProcessLaunchResult Launch(ProcessRequest request)
        {
            LastRequest = request;
            return new ProcessLaunchResult(true, 123, "explorer.exe");
        }
    }
}
