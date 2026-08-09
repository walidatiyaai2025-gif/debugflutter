using System.Collections.Generic;

namespace FlutterBuildDoctor.Application.Environment;

public sealed class EnvironmentReadinessReport
{
    public IReadOnlyList<object> Tools { get; init; } = new List<object>();
    public bool IsReady { get; init; }
    public string Summary { get; init; } = string.Empty;
}
