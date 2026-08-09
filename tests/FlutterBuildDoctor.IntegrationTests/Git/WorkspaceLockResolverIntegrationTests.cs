using System.Diagnostics;
using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.IntegrationTests.Git;

public sealed class WorkspaceLockResolverIntegrationTests
{
    [Fact]
    public async Task ReleaseLocks_WhenChildProcessHoldsRepositoryFile_TerminatesOwnerAndAllowsMove()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "FlutterBuildDoctorTests",
            Guid.NewGuid().ToString("N"));
        var repositoryPath = Path.Combine(root, "repo");
        var movedPath = Path.Combine(root, "repo-moved");
        var lockedFile = Path.Combine(repositoryPath, "locked.txt");

        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(lockedFile, "locked");

        using var process = StartLockingProcess(lockedFile);

        try
        {
            var ready = await process.StandardOutput
                .ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal("LOCKED", ready);
            Assert.False(process.HasExited);

            var resolver = new WindowsRestartManagerWorkspaceLockResolver();
            var result = resolver.ReleaseLocks(repositoryPath);

            Assert.True(result.Supported);
            Assert.Contains(
                result.Processes,
                owner => owner.ProcessId == process.Id && owner.Terminated);
            Assert.True(process.WaitForExit(10_000));

            Directory.Move(repositoryPath, movedPath);
            Assert.True(File.Exists(Path.Combine(movedPath, "locked.txt")));
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(10_000);
                }
            }
            catch
            {
                // Best-effort fixture cleanup only.
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static Process StartLockingProcess(string filePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolvePowerShellExecutable(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.Environment["FBD_LOCK_FILE"] = filePath;
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "$path = [Environment]::GetEnvironmentVariable('FBD_LOCK_FILE'); " +
            "$script:stream = [System.IO.FileStream]::new($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None); " +
            "[Console]::Out.WriteLine('LOCKED'); [Console]::Out.Flush(); " +
            "try { Start-Sleep -Seconds 60 } finally { $script:stream.Dispose() }");

        var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        return process;
    }

    private static string ResolvePowerShellExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pwsh = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        return File.Exists(pwsh) ? pwsh : "powershell.exe";
    }
}
