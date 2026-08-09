namespace FlutterBuildDoctor.Application.Processes;

public interface IProcessSecretRedactor
{
    string SanitizeCommand(ProcessRequest request);

    string RedactText(string value, ProcessRequest request);
}
