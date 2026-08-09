namespace FlutterBuildDoctor.Git.Repository;

public interface IGitRefreshFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    string GetFullPath(string path);

    void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName);

    void DeleteDirectory(string path, bool recursive);
}

public sealed class GitRefreshFileSystem : IGitRefreshFileSystem
{
    public bool DirectoryExists(string path)
        => Directory.Exists(path);

    public bool FileExists(string path)
        => File.Exists(path);

    public string GetFullPath(string path)
        => Path.GetFullPath(path);

    public void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName)
        => Directory.Move(sourceDirectoryName, destinationDirectoryName);

    public void DeleteDirectory(string path, bool recursive)
        => Directory.Delete(path, recursive);
}
