using FlutterBuildDoctor.Domain.Environment;

namespace FlutterBuildDoctor.Application.Services;

public interface IEnvironmentScanner
{
    Task<IReadOnlyList<ToolStatus>> ScanAsync(CancellationToken cancellationToken = default);
}
