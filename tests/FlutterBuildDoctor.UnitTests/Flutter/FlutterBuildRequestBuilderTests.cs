using FlutterBuildDoctor.Flutter.Build;
using FlutterBuildDoctor.Flutter.Commands;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterBuildRequestBuilderTests
{
    private static readonly FlutterCommandContext Context = new("flutter", @"C:\work\app");
    private readonly FlutterBuildRequestBuilder _builder = new();

    [Theory]
    [InlineData(FlutterBuildMode.Debug, "build|apk|--debug")]
    [InlineData(FlutterBuildMode.Profile, "build|apk|--profile")]
    [InlineData(FlutterBuildMode.Release, "build|apk|--release")]
    public void Build_ApkModesUseTypedArguments(FlutterBuildMode mode, string expected)
    {
        var request = _builder.Build(new FlutterBuildRequest(Context, FlutterBuildArtifactType.Apk, mode));

        Assert.Equal(expected, string.Join("|", request.Arguments));
        Assert.Equal("flutter", request.FileName);
        Assert.NotNull(request.Timeout);
    }

    [Fact]
    public void Build_ReleaseAppBundleUsesAppBundleCommand()
    {
        var request = _builder.Build(new FlutterBuildRequest(
            Context,
            FlutterBuildArtifactType.AppBundle,
            FlutterBuildMode.Release));

        Assert.Equal(new[] { "build", "appbundle", "--release" }, request.Arguments);
    }

    [Fact]
    public void Build_AddsFlavorAndTargetAsSeparateArguments()
    {
        var request = _builder.Build(new FlutterBuildRequest(
            Context,
            FlutterBuildArtifactType.Apk,
            FlutterBuildMode.Release,
            "production",
            "lib/main_production.dart"));

        Assert.Equal(
            new[] { "build", "apk", "--release", "--flavor", "production", "--target", "lib/main_production.dart" },
            request.Arguments);
    }

    [Fact]
    public void Build_RejectsNonReleaseAppBundleProfile()
    {
        Assert.Throws<ArgumentException>(() => _builder.Build(new FlutterBuildRequest(
            Context,
            FlutterBuildArtifactType.AppBundle,
            FlutterBuildMode.Profile)));
    }
}
