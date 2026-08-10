using FlutterBuildDoctor.Flutter.Commands;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterCommandBuilderTests
{
    private static readonly FlutterCommandContext Context = new("flutter", @"C:\work\app");
    private readonly FlutterCommandBuilder _builder = new();

    [Theory]
    [InlineData(FlutterCommandOperation.PubGet, "pub|get")]
    [InlineData(FlutterCommandOperation.Clean, "clean")]
    [InlineData(FlutterCommandOperation.Analyze, "analyze")]
    [InlineData(FlutterCommandOperation.Test, "test")]
    [InlineData(FlutterCommandOperation.PubOutdated, "pub|outdated")]
    [InlineData(FlutterCommandOperation.Devices, "devices|--machine")]
    [InlineData(FlutterCommandOperation.Emulators, "emulators")]
    public void Build_UsesTypedArgumentsAndFiniteTimeout(
        FlutterCommandOperation operation,
        string expectedArguments)
    {
        var request = _builder.Build(operation, Context);

        Assert.Equal("flutter", request.FileName);
        Assert.Equal(@"C:\work\app", request.WorkingDirectory);
        Assert.Equal(expectedArguments, string.Join("|", request.Arguments));
        Assert.NotNull(request.Timeout);
        Assert.True(request.Timeout > TimeSpan.Zero);
    }

    [Fact]
    public void BuildRun_UsesDeviceFlavorAndTargetAsSeparateArguments()
    {
        var request = _builder.BuildRun(new FlutterRunRequest(
            Context,
            "emulator-5554",
            "staging",
            "lib/main_staging.dart"));

        Assert.Equal(
            new[] { "run", "-d", "emulator-5554", "--flavor", "staging", "-t", "lib/main_staging.dart" },
            request.Arguments);
        Assert.Null(request.Timeout);
        Assert.Equal("flutter run", request.DisplayName);
    }

    [Fact]
    public void BuildRun_RejectsControlCharactersInUserSuppliedValues()
    {
        var request = new FlutterRunRequest(Context, "emulator-5554\r\n--verbose");

        Assert.Throws<ArgumentException>(() => _builder.BuildRun(request));
    }

    [Fact]
    public void Build_RunRequiresExplicitTargetRequest()
    {
        Assert.Throws<InvalidOperationException>(() => _builder.Build(FlutterCommandOperation.Run, Context));
    }
}
