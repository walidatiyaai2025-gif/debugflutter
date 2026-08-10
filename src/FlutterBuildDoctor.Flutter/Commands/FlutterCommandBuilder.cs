using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Commands;

public sealed class FlutterCommandBuilder : IFlutterCommandBuilder
{
    public ProcessRequest Build(FlutterCommandOperation operation, FlutterCommandContext context)
        => operation switch
        {
            FlutterCommandOperation.PubGet => Create(
                context,
                new[] { "pub", "get" },
                TimeSpan.FromMinutes(5),
                "flutter pub get"),
            FlutterCommandOperation.Clean => Create(
                context,
                new[] { "clean" },
                TimeSpan.FromMinutes(2),
                "flutter clean"),
            FlutterCommandOperation.Analyze => Create(
                context,
                new[] { "analyze" },
                TimeSpan.FromMinutes(5),
                "flutter analyze"),
            FlutterCommandOperation.Test => Create(
                context,
                new[] { "test" },
                TimeSpan.FromMinutes(15),
                "flutter test"),
            FlutterCommandOperation.PubOutdated => Create(
                context,
                new[] { "pub", "outdated" },
                TimeSpan.FromMinutes(5),
                "flutter pub outdated"),
            FlutterCommandOperation.Devices => Create(
                context,
                new[] { "devices", "--machine" },
                TimeSpan.FromMinutes(1),
                "flutter devices --machine"),
            FlutterCommandOperation.Emulators => Create(
                context,
                new[] { "emulators" },
                TimeSpan.FromMinutes(1),
                "flutter emulators"),
            FlutterCommandOperation.Run => throw new InvalidOperationException(
                "Use BuildRun with an explicit target device for flutter run."),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown Flutter command operation.")
        };

    public ProcessRequest BuildRun(FlutterRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateContext(request.Context);
        var deviceId = RequireSafeValue(request.DeviceId, nameof(request.DeviceId));

        var arguments = new List<string> { "run", "-d", deviceId };
        if (!string.IsNullOrWhiteSpace(request.Flavor))
        {
            arguments.Add("--flavor");
            arguments.Add(RequireSafeValue(request.Flavor, nameof(request.Flavor)));
        }

        if (!string.IsNullOrWhiteSpace(request.Target))
        {
            arguments.Add("-t");
            arguments.Add(RequireSafeValue(request.Target, nameof(request.Target)));
        }

        return new ProcessRequest(
            request.Context.FlutterExecutable,
            arguments,
            request.Context.WorkingDirectory,
            Timeout: null,
            DisplayName: "flutter run");
    }

    private static ProcessRequest Create(
        FlutterCommandContext context,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string displayName)
    {
        ValidateContext(context);
        return new ProcessRequest(
            context.FlutterExecutable,
            arguments,
            context.WorkingDirectory,
            Timeout: timeout,
            DisplayName: displayName);
    }

    private static void ValidateContext(FlutterCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RequireSafeValue(context.FlutterExecutable, nameof(context.FlutterExecutable));
        RequireSafeValue(context.WorkingDirectory, nameof(context.WorkingDirectory));
    }

    private static string RequireSafeValue(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("Control characters are not allowed in Flutter command arguments.", parameterName);
        }

        return value;
    }
}
