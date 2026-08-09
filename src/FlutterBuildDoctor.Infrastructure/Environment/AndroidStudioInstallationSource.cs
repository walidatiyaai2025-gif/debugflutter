using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Infrastructure.Environment;

public sealed record AndroidStudioSearchRoot(string Path, AndroidStudioDiscoverySource Source, bool Recursive, int MaxDepth = 5);

public interface IAndroidStudioSearchRootProvider
{
    IReadOnlyList<AndroidStudioSearchRoot> GetRoots();
}

public interface IAndroidStudioInstallationSource
{
    IReadOnlyList<AndroidStudioExecutableEvidence> Discover();
}

public sealed class SystemAndroidStudioSearchRootProvider : IAndroidStudioSearchRootProvider
{
    public IReadOnlyList<AndroidStudioSearchRoot> GetRoots()
    {
        var roots = new List<AndroidStudioSearchRoot>();
        AddDirectRoot(roots, System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), AndroidStudioDiscoverySource.ProgramFiles);
        AddDirectRoot(roots, System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86), AndroidStudioDiscoverySource.ProgramFilesX86);

        var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            roots.Add(new AndroidStudioSearchRoot(Path.Combine(localAppData, "Programs", "Android Studio"), AndroidStudioDiscoverySource.LocalAppDataPrograms, Recursive: false));
            roots.Add(new AndroidStudioSearchRoot(Path.Combine(localAppData, "JetBrains", "Toolbox", "apps", "AndroidStudio"), AndroidStudioDiscoverySource.JetBrainsToolbox, Recursive: true, MaxDepth: 6));
        }
        return roots;
    }

    private static void AddDirectRoot(ICollection<AndroidStudioSearchRoot> roots, string basePath, AndroidStudioDiscoverySource source)
    {
        if (!string.IsNullOrWhiteSpace(basePath))
            roots.Add(new AndroidStudioSearchRoot(Path.Combine(basePath, "Android", "Android Studio"), source, Recursive: false));
    }
}

public sealed class WindowsAndroidStudioInstallationSource : IAndroidStudioInstallationSource
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private readonly IAndroidStudioSearchRootProvider _rootProvider;

    public WindowsAndroidStudioInstallationSource(IAndroidStudioSearchRootProvider rootProvider)
        => _rootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));

    public IReadOnlyList<AndroidStudioExecutableEvidence> Discover()
    {
        var results = new Dictionary<string, AndroidStudioExecutableEvidence>(PathComparer);
        foreach (var root in _rootProvider.GetRoots())
        {
            if (string.IsNullOrWhiteSpace(root.Path) || !Directory.Exists(root.Path)) continue;
            if (root.Recursive) DiscoverRecursive(root, results); else AddExecutableFromInstallationRoot(root.Path, root.Source, results);
        }
        return results.Values.OrderBy(e => e.DiscoverySource).ThenBy(e => e.ExecutablePath, PathComparer).ToArray();
    }

    private static void DiscoverRecursive(AndroidStudioSearchRoot root, IDictionary<string, AndroidStudioExecutableEvidence> results)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root.Path, 0));
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            AddExecutableFromInstallationRoot(current.Path, root.Source, results);
            if (current.Depth >= root.MaxDepth) continue;
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(current.Path).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var child in children) pending.Enqueue((child, current.Depth + 1));
        }
    }

    private static void AddExecutableFromInstallationRoot(string installationRoot, AndroidStudioDiscoverySource source, IDictionary<string, AndroidStudioExecutableEvidence> results)
    {
        var bin = string.Equals(Path.GetFileName(installationRoot), "bin", StringComparison.OrdinalIgnoreCase) ? installationRoot : Path.Combine(installationRoot, "bin");
        foreach (var fileName in new[] { "studio64.exe", "studio.exe" })
        {
            var candidate = Path.Combine(bin, fileName);
            if (!File.Exists(candidate)) continue;
            var fullPath = Path.GetFullPath(candidate);
            results.TryAdd(fullPath, new AndroidStudioExecutableEvidence(fullPath, source));
            break;
        }
    }
}
