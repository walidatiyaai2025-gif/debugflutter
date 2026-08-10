using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Build;

public sealed class FlutterBuildRequestBuilder : IFlutterBuildRequestBuilder
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(30);

    public ProcessRequest Build(FlutterBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateValue(request.Context.FlutterExecutable, nameof(request.Context.FlutterExecutable));
        ValidateValue(request.Context.WorkingDirectory, nameof(request.Context.WorkingDirectory));

        if (request.ArtifactType == FlutterBuildArtifactType.AppBundle && request.Mode != FlutterBuildMode.Release)
        {
            throw new ArgumentException("App Bundle builds are release-only in the current build profile contract.", nameof(request));
        }

        var arguments = new List<string>
        {
            "build",
            request.ArtifactType == FlutterBuildArtifactType.Apk ? "apk" : "appbundle",
            ModeArgument(request.Mode)
        };

        if (!string.IsNullOrWhiteSpace(request.Flavor))
        {
            arguments.Add("--flavor");
            arguments.Add(ValidateValue(request.Flavor, nameof(request.Flavor)));
        }

        if (!string.IsNullOrWhiteSpace(request.Target))
        {
            arguments.Add("--target");
            arguments.Add(ValidateValue(request.Target, nameof(request.Target)));
        }

        return new ProcessRequest(
            request.Context.FlutterExecutable,
            arguments,
            request.Context.WorkingDirectory,
            Timeout: BuildTimeout,
            DisplayName: $"flutter build {ArtifactName(request.ArtifactType)} {ModeArgument(request.Mode)}");
    }

    private static string ModeArgument(FlutterBuildMode mode)
        => mode switch
        {
            FlutterBuildMode.Debug => "--debug",
            FlutterBuildMode.Profile => "--profile",
            FlutterBuildMode.Release => "--release",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Flutter build mode.")
        };

    private static string ArtifactName(FlutterBuildArtifactType type)
        => type switch
        {
            FlutterBuildArtifactType.Apk => "apk",
            FlutterBuildArtifactType.AppBundle => "appbundle",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown Flutter build artifact type.")
        };

    private static string ValidateValue(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("Control characters are not allowed in Flutter build arguments.", parameterName);
        }

        return value;
    }
}
