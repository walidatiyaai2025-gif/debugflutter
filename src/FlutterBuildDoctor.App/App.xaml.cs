using System.Windows;
using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.App.Errors;
using FlutterBuildDoctor.App.Logging;
using FlutterBuildDoctor.App.Services;
using FlutterBuildDoctor.Application.Errors;
using FlutterBuildDoctor.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace FlutterBuildDoctor.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private GlobalExceptionHooks? _exceptionHooks;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddFlutterBuildDoctorLogging();
                    services.AddFlutterBuildDoctorExceptionHandling();
                    services.AddFlutterBuildDoctorRuntimeDetection();
                    services.AddFlutterBuildDoctorPresentation();
                })
                .UseSerilog((_, services, loggerConfiguration) =>
                {
                    var inAppSink = services.GetRequiredService<AppLogStoreSink>();

                    loggerConfiguration
                        .MinimumLevel.Debug()
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                        .Enrich.FromLogContext()
                        .Enrich.WithProperty("Application", "FlutterBuildDoctor")
                        .WriteTo.File(
                            LoggingPaths.EnsureLogFilePath(),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 14,
                            shared: true,
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                        .WriteTo.Sink(inAppSink);
                })
                .Build();

            _exceptionHooks = _host.Services.GetRequiredService<GlobalExceptionHooks>();
            _exceptionHooks.Attach(this);

            await _host.StartAsync();

            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            var identity = _host.Services.GetRequiredService<IApplicationIdentityService>().Current;
            logger.LogInformation(
                "Flutter Build Doctor host started. Version={ProductVersion} Build={BuildNumber} Commit={CommitSha}",
                identity.ProductVersion,
                identity.BuildNumber,
                identity.ShortCommit);

            _host.Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception exception)
        {
            _host?.Services.GetService<IAppExceptionReporter>()
                ?.Report(exception, AppExceptionSource.HostStartup, isTerminating: true);

            MessageBox.Show(
                "Flutter Build Doctor could not start. Check the application log for diagnostic details.",
                "Flutter Build Doctor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                _host.Services.GetService<ILogger<App>>()?.LogInformation("Flutter Build Doctor host stopping");
                await _host.StopAsync();
            }
            catch (Exception exception)
            {
                _host.Services.GetService<IAppExceptionReporter>()
                    ?.Report(exception, AppExceptionSource.HostShutdown, isTerminating: true);
            }
            finally
            {
                _exceptionHooks?.Detach();
                _host.Dispose();
                _exceptionHooks = null;
                _host = null;
            }
        }

        base.OnExit(e);
    }
}
