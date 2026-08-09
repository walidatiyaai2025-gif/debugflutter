namespace FlutterBuildDoctor.Application.Errors;

public enum AppExceptionSource
{
    HostStartup,
    HostShutdown,
    Dispatcher,
    AppDomain,
    UnobservedTask,
}
