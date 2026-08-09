using FlutterBuildDoctor.Android.ProjectAnalysis;
using FlutterBuildDoctor.App.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class GradleDslDetectorIntegrationTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonGradleDslDetector()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IGradleDslDetector>();
        var second = provider.GetRequiredService<IGradleDslDetector>();

        Assert.IsType<GradleDslDetector>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Detect_ReadsLayoutWithoutChangingGradleFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-gradle-dsl-integration-" + Guid.NewGuid().ToString("N"));
        var android = Path.Combine(root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);

        var settings = Path.Combine(android, "settings.gradle.kts");
        var projectBuild = Path.Combine(android, "build.gradle.kts");
        var appBuild = Path.Combine(app, "build.gradle.kts");
        File.WriteAllText(settings, "pluginManagement {}\n");
        File.WriteAllText(projectBuild, "plugins {}\n");
        File.WriteAllText(appBuild, "android {}\n");

        var before = new Dictionary<string, (byte[] Bytes, DateTime WriteTime)>
        {
            [settings] = (File.ReadAllBytes(settings), File.GetLastWriteTimeUtc(settings)),
            [projectBuild] = (File.ReadAllBytes(projectBuild), File.GetLastWriteTimeUtc(projectBuild)),
            [appBuild] = (File.ReadAllBytes(appBuild), File.GetLastWriteTimeUtc(appBuild))
        };

        try
        {
            var result = new GradleDslDetector().Detect(root);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(GradleDslKind.Kotlin, result.EffectiveDsl);
            Assert.Equal(3, result.Scripts.Count);
            foreach (var entry in before)
            {
                Assert.Equal(entry.Value.Bytes, File.ReadAllBytes(entry.Key));
                Assert.Equal(entry.Value.WriteTime, File.GetLastWriteTimeUtc(entry.Key));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
