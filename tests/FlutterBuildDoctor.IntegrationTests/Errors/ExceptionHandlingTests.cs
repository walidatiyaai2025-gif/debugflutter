using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.App.ViewModels;
using FlutterBuildDoctor.Application.Errors;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Errors;

public sealed class ExceptionHandlingTests
{
    [Fact]
    public void Reporter_StoresSafeRecordAndSurfacesReferenceToShell()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorExceptionHandling();
        services.AddFlutterBuildDoctorRuntimeDetection();
        services.AddFlutterBuildDoctorPresentation();

        using var provider = services.BuildServiceProvider();
        var reporter = provider.GetRequiredService<IAppExceptionReporter>();
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();

        var record = reporter.Report(
            new InvalidOperationException("secret-token-must-not-reach-ui"),
            AppExceptionSource.Dispatcher);

        Assert.Same(record, reporter.Latest);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.Equal(record.UserMessage, viewModel.StatusMessage);
        Assert.Contains(record.Id.ToString("N"), viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-token-must-not-reach-ui", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Reporter_UsesCriticalSafeMessageForTerminatingFailure()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorExceptionHandling();

        using var provider = services.BuildServiceProvider();
        var reporter = provider.GetRequiredService<IAppExceptionReporter>();

        var record = reporter.Report(
            new InvalidOperationException("internal detail"),
            AppExceptionSource.AppDomain,
            isTerminating: true);

        Assert.True(record.IsTerminating);
        Assert.Contains("critical error", record.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal detail", record.UserMessage, StringComparison.Ordinal);
    }
}
