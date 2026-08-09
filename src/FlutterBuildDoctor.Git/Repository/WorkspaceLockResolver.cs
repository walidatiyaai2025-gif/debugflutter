using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FlutterBuildDoctor.Git.Repository;

public sealed record WorkspaceLockProcessResult(
    int ProcessId,
    string? ProcessName,
    bool Terminated,
    string? FailureReason = null);

public sealed record WorkspaceLockReleaseResult(
    bool Supported,
    int RegisteredFileCount,
    IReadOnlyList<WorkspaceLockProcessResult> Processes,
    string Message)
{
    public int TerminatedCount => Processes.Count(process => process.Terminated);

    public int UnresolvedCount => Processes.Count(process => !process.Terminated);
}

public interface IGitWorkspaceLockResolver
{
    WorkspaceLockReleaseResult ReleaseLocks(string repositoryPath);
}

/// <summary>
/// Uses the Windows Restart Manager API to discover processes that currently own
/// file handles inside a repository, then terminates only those discovered owners.
/// Critical Windows processes and Flutter Build Doctor itself are never terminated.
/// </summary>
public sealed class WindowsRestartManagerWorkspaceLockResolver : IGitWorkspaceLockResolver
{
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;
    private const int RegistrationBatchSize = 128;
    private const int MaximumRegisteredFiles = 12_000;
    private const int ProcessExitWaitMilliseconds = 5_000;

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "Registry",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "winlogon",
        "dwm"
    };

    public WorkspaceLockReleaseResult ReleaseLocks(string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (!OperatingSystem.IsWindows())
        {
            return new WorkspaceLockReleaseResult(
                Supported: false,
                RegisteredFileCount: 0,
                Processes: Array.Empty<WorkspaceLockProcessResult>(),
                Message: "Workspace lock recovery is only available on Windows.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(repositoryPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure($"Workspace lock recovery could not normalize the repository path: {ex.Message}");
        }

        if (!Directory.Exists(fullPath))
        {
            return Failure("Workspace lock recovery could not start because the repository directory no longer exists.");
        }

        string[] candidateFiles;
        try
        {
            candidateFiles = EnumerateCandidateFiles(fullPath)
                .Take(MaximumRegisteredFiles)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException)
        {
            return Failure($"Workspace lock recovery could not enumerate repository files: {ex.Message}");
        }

        if (candidateFiles.Length == 0)
        {
            return new WorkspaceLockReleaseResult(
                Supported: true,
                RegisteredFileCount: 0,
                Processes: Array.Empty<WorkspaceLockProcessResult>(),
                Message: "No repository files were available for Windows lock inspection.");
        }

        IReadOnlyList<RestartManagerProcess> lockingProcesses;
        try
        {
            lockingProcesses = FindLockingProcesses(candidateFiles);
        }
        catch (Win32Exception ex)
        {
            return Failure(
                $"Windows Restart Manager could not inspect repository locks: {ex.Message}",
                candidateFiles.Length);
        }

        if (lockingProcesses.Count == 0)
        {
            return new WorkspaceLockReleaseResult(
                Supported: true,
                RegisteredFileCount: candidateFiles.Length,
                Processes: Array.Empty<WorkspaceLockProcessResult>(),
                Message: $"Windows lock inspection registered {candidateFiles.Length} repository file(s) but found no owning process.");
        }

        var results = new List<WorkspaceLockProcessResult>(lockingProcesses.Count);
        foreach (var owner in lockingProcesses.DistinctBy(process => process.ProcessId))
        {
            results.Add(TerminateLockOwner(owner));
        }

        var terminatedNames = results
            .Where(result => result.Terminated)
            .Select(result => string.IsNullOrWhiteSpace(result.ProcessName)
                ? $"PID {result.ProcessId}"
                : $"{result.ProcessName} (PID {result.ProcessId})")
            .ToArray();

        var message = terminatedNames.Length > 0
            ? $"Terminated {terminatedNames.Length} workspace lock owner(s): {string.Join(", ", terminatedNames)}."
            : "Workspace lock owners were detected, but none could be terminated safely.";

        if (results.Any(result => !result.Terminated))
        {
            message += $" {results.Count(result => !result.Terminated)} owner(s) remain unresolved.";
        }

        return new WorkspaceLockReleaseResult(
            Supported: true,
            RegisteredFileCount: candidateFiles.Length,
            Processes: results,
            Message: message);
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string repositoryPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        foreach (var preferred in EnumeratePreferredFiles(repositoryPath, options))
        {
            if (seen.Add(preferred))
            {
                yield return preferred;
            }
        }

        foreach (var file in Directory.EnumerateFiles(repositoryPath, "*", options))
        {
            if (seen.Add(file))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumeratePreferredFiles(
        string repositoryPath,
        EnumerationOptions options)
    {
        foreach (var relativePath in new[]
                 {
                     Path.Combine(".git", "index"),
                     Path.Combine(".git", "HEAD"),
                     Path.Combine(".git", "config"),
                     "pubspec.yaml",
                     "pubspec.lock"
                 })
        {
            var path = Path.Combine(repositoryPath, relativePath);
            if (File.Exists(path))
            {
                yield return path;
            }
        }

        var gitDirectory = Path.Combine(repositoryPath, ".git");
        if (Directory.Exists(gitDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(gitDirectory, "*", options))
            {
                yield return file;
            }
        }
    }

    private static IReadOnlyList<RestartManagerProcess> FindLockingProcesses(string[] candidateFiles)
    {
        var sessionKey = Guid.NewGuid().ToString("N");
        var startResult = RestartManagerNative.RmStartSession(out var sessionHandle, 0, sessionKey);
        if (startResult != ErrorSuccess)
        {
            throw new Win32Exception(startResult);
        }

        try
        {
            foreach (var batch in candidateFiles.Chunk(RegistrationBatchSize))
            {
                var registrationResult = RestartManagerNative.RmRegisterResources(
                    sessionHandle,
                    (uint)batch.Length,
                    batch,
                    0,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero);

                if (registrationResult != ErrorSuccess)
                {
                    throw new Win32Exception(registrationResult);
                }
            }

            return GetRegisteredLockOwners(sessionHandle);
        }
        finally
        {
            RestartManagerNative.RmEndSession(sessionHandle);
        }
    }

    private static IReadOnlyList<RestartManagerProcess> GetRegisteredLockOwners(uint sessionHandle)
    {
        uint processInfoNeeded = 0;
        uint processInfoCount = 0;
        uint rebootReasons = 0;

        var listResult = RestartManagerNative.RmGetList(
            sessionHandle,
            out processInfoNeeded,
            ref processInfoCount,
            null,
            ref rebootReasons);

        if (listResult == ErrorSuccess || processInfoNeeded == 0)
        {
            return Array.Empty<RestartManagerProcess>();
        }

        if (listResult != ErrorMoreData)
        {
            throw new Win32Exception(listResult);
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var processInfo = new RestartManagerNative.RmProcessInfo[processInfoNeeded];
            processInfoCount = processInfoNeeded;

            listResult = RestartManagerNative.RmGetList(
                sessionHandle,
                out processInfoNeeded,
                ref processInfoCount,
                processInfo,
                ref rebootReasons);

            if (listResult == ErrorSuccess)
            {
                return processInfo
                    .Take((int)processInfoCount)
                    .Select(info => new RestartManagerProcess(
                        info.Process.ProcessId,
                        info.ApplicationName))
                    .ToArray();
            }

            if (listResult != ErrorMoreData)
            {
                throw new Win32Exception(listResult);
            }
        }

        throw new Win32Exception(ErrorMoreData, "The set of processes locking the workspace changed repeatedly during inspection.");
    }

    private static WorkspaceLockProcessResult TerminateLockOwner(RestartManagerProcess owner)
    {
        if (owner.ProcessId <= 4 || owner.ProcessId == Environment.ProcessId)
        {
            return new WorkspaceLockProcessResult(
                owner.ProcessId,
                owner.ApplicationName,
                Terminated: false,
                FailureReason: "Protected process was not terminated.");
        }

        try
        {
            using var process = Process.GetProcessById(owner.ProcessId);
            var processName = TryGetProcessName(process) ?? owner.ApplicationName;

            if (string.IsNullOrWhiteSpace(processName) || ProtectedProcessNames.Contains(processName))
            {
                return new WorkspaceLockProcessResult(
                    owner.ProcessId,
                    processName,
                    Terminated: false,
                    FailureReason: "Protected or unidentified process was not terminated.");
            }

            if (process.HasExited)
            {
                return new WorkspaceLockProcessResult(owner.ProcessId, processName, Terminated: true);
            }

            process.Kill(entireProcessTree: true);
            var exited = process.WaitForExit(ProcessExitWaitMilliseconds);

            return exited
                ? new WorkspaceLockProcessResult(owner.ProcessId, processName, Terminated: true)
                : new WorkspaceLockProcessResult(
                    owner.ProcessId,
                    processName,
                    Terminated: false,
                    FailureReason: "Process did not exit within the lock-recovery timeout.");
        }
        catch (ArgumentException)
        {
            return new WorkspaceLockProcessResult(
                owner.ProcessId,
                owner.ApplicationName,
                Terminated: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return new WorkspaceLockProcessResult(
                owner.ProcessId,
                owner.ApplicationName,
                Terminated: false,
                FailureReason: ex.Message);
        }
    }

    private static string? TryGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return null;
        }
    }

    private static WorkspaceLockReleaseResult Failure(string message, int registeredFileCount = 0)
        => new(
            Supported: true,
            RegisteredFileCount: registeredFileCount,
            Processes: Array.Empty<WorkspaceLockProcessResult>(),
            Message: message);

    private sealed record RestartManagerProcess(int ProcessId, string? ApplicationName);

    private static class RestartManagerNative
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct RmUniqueProcess
        {
            public int ProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        internal enum RmApplicationType
        {
            Unknown = 0,
            MainWindow = 1,
            OtherWindow = 2,
            Service = 3,
            Explorer = 4,
            Console = 5,
            Critical = 1000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct RmProcessInfo
        {
            public RmUniqueProcess Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ApplicationName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string ServiceShortName;

            public RmApplicationType ApplicationType;
            public uint ApplicationStatus;
            public uint TerminalServicesSessionId;

            [MarshalAs(UnmanagedType.Bool)]
            public bool Restartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        internal static extern int RmStartSession(
            out uint sessionHandle,
            int sessionFlags,
            string sessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        internal static extern int RmRegisterResources(
            uint sessionHandle,
            uint fileCount,
            [In] string[] fileNames,
            uint applicationCount,
            IntPtr applications,
            uint serviceCount,
            IntPtr serviceNames);

        [DllImport("rstrtmgr.dll")]
        internal static extern int RmGetList(
            uint sessionHandle,
            out uint processInfoNeeded,
            ref uint processInfoCount,
            [In, Out] RmProcessInfo[]? processInfo,
            ref uint rebootReasons);

        [DllImport("rstrtmgr.dll")]
        internal static extern int RmEndSession(uint sessionHandle);
    }
}
