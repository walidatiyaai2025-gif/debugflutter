namespace FlutterBuildDoctor.Application.Environment;

public sealed class EnvironmentReportBuilder
{
    public IReadOnlyList<object> Build(IEnumerable<object> results)
    {
        return results.ToList();
    }
}
