namespace FlutterBuildDoctor.Application.Environment;

public sealed record EnvironmentScanResult(
    IReadOnlyList<Domain.Environment.ToolStatus> Tools);
