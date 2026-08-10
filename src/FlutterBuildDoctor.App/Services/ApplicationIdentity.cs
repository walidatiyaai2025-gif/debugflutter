namespace FlutterBuildDoctor.App.Services;

public sealed record ApplicationIdentity(
    string ProductVersion,
    string BuildNumber,
    string? CommitSha)
{
    public string ShortCommit => string.IsNullOrWhiteSpace(CommitSha)
        ? "local"
        : CommitSha[..Math.Min(12, CommitSha.Length)];

    public string DisplayText => $"v{ProductVersion} • {ShortCommit} • build {BuildNumber}";
}

public interface IApplicationIdentityService
{
    ApplicationIdentity Current { get; }
}
