using System.Windows;
using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.App.Logging;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddFlutterBuildDoctorLogging();
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

        await _host.StartAsync();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Flutter Build Doctor host started");

        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.Services.GetService<ILogger<App>>()?.LogInformation("Flutter Build Doctor host stopping");
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
