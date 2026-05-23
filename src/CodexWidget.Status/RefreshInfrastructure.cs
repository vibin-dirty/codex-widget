using System.Globalization;

namespace CodexWidget.Status;

internal interface IAsyncDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class TaskAsyncDelay : IAsyncDelay
{
    public static TaskAsyncDelay Instance { get; } = new();

    private TaskAsyncDelay()
    {
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}

internal readonly record struct FileMetadataSnapshot(
    bool Exists,
    long Length,
    DateTimeOffset LastWriteUtc);

internal interface IProfileFileSystem
{
    FileMetadataSnapshot GetMetadata(string path);

    IReadOnlyList<string> EnumerateSavedProfileJsonFiles(string profilesDirectory);
}

internal sealed class SystemProfileFileSystem : IProfileFileSystem
{
    public static SystemProfileFileSystem Instance { get; } = new();

    private SystemProfileFileSystem()
    {
    }

    public FileMetadataSnapshot GetMetadata(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return default;
        }

        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return default;
            }

            return new FileMetadataSnapshot(
                Exists: true,
                Length: fileInfo.Length,
                LastWriteUtc: fileInfo.LastWriteTimeUtc);
        }
        catch
        {
            return default;
        }
    }

    public IReadOnlyList<string> EnumerateSavedProfileJsonFiles(string profilesDirectory)
    {
        if (string.IsNullOrWhiteSpace(profilesDirectory) || !Directory.Exists(profilesDirectory))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateFiles(profilesDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var fileName = Path.GetFileName(path);
                    return !fileName.Equals("profiles.json", StringComparison.OrdinalIgnoreCase)
                           && !fileName.Equals("update.json", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

internal interface IProfileFileWatcherFactory
{
    IProfileFileWatcher? TryCreate(
        ProfileChangeMonitorPaths paths,
        Action<string> onChanged,
        Action<Exception> onError);
}

internal interface IProfileFileWatcher : IDisposable
{
    void Start();
}

internal sealed class FileSystemProfileFileWatcherFactory : IProfileFileWatcherFactory
{
    public static FileSystemProfileFileWatcherFactory Instance { get; } = new();

    private FileSystemProfileFileWatcherFactory()
    {
    }

    public IProfileFileWatcher? TryCreate(
        ProfileChangeMonitorPaths paths,
        Action<string> onChanged,
        Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentNullException.ThrowIfNull(onError);

        var watchers = new List<FileSystemWatcher>();

        var codexDirectory = ResolveCodexDirectory(paths);
        if (!string.IsNullOrWhiteSpace(codexDirectory) && Directory.Exists(codexDirectory))
        {
            watchers.Add(CreateWatcher(codexDirectory, "*", onChanged, onError));
        }

        if (!string.IsNullOrWhiteSpace(paths.ProfilesDirectory) && Directory.Exists(paths.ProfilesDirectory))
        {
            watchers.Add(CreateWatcher(paths.ProfilesDirectory, "*.json", onChanged, onError));
            watchers.Add(CreateWatcher(paths.ProfilesDirectory, "*", onChanged, onError));
        }

        if (watchers.Count == 0)
        {
            return null;
        }

        return new CompositeProfileFileWatcher(watchers);
    }

    private static string? ResolveCodexDirectory(ProfileChangeMonitorPaths paths)
    {
        return GetExistingDirectory(paths.CurrentAuthPath)
            ?? GetExistingDirectory(paths.ConfigPath)
            ?? GetExistingDirectory(paths.ProfilesDirectory);
    }

    private static string? GetExistingDirectory(string? fileOrDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(fileOrDirectoryPath))
        {
            return null;
        }

        if (Directory.Exists(fileOrDirectoryPath))
        {
            return fileOrDirectoryPath;
        }

        return Path.GetDirectoryName(fileOrDirectoryPath);
    }

    private static FileSystemWatcher CreateWatcher(
        string directoryPath,
        string filter,
        Action<string> onChanged,
        Action<Exception> onError)
    {
        var watcher = new FileSystemWatcher(directoryPath, filter)
        {
            NotifyFilter = NotifyFilters.FileName
                           | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite
                           | NotifyFilters.Size
                           | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
        };

        void HandleEvent(string fullPath)
        {
            try
            {
                onChanged(fullPath);
            }
            catch (Exception exception)
            {
                onError(exception);
            }
        }

        watcher.Changed += (_, args) => HandleEvent(args.FullPath);
        watcher.Created += (_, args) => HandleEvent(args.FullPath);
        watcher.Deleted += (_, args) => HandleEvent(args.FullPath);
        watcher.Renamed += (_, args) =>
        {
            HandleEvent(args.OldFullPath);
            HandleEvent(args.FullPath);
        };
        watcher.Error += (_, args) =>
        {
            var exception = args.GetException() ?? new IOException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "FileSystemWatcher reported an unknown error for '{0}'.",
                    directoryPath));
            onError(exception);
        };

        return watcher;
    }

    private sealed class CompositeProfileFileWatcher(IReadOnlyList<FileSystemWatcher> watchers) : IProfileFileWatcher
    {
        private bool disposed;

        public void Start()
        {
            ThrowIfDisposed();

            foreach (var watcher in watchers)
            {
                watcher.EnableRaisingEvents = true;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (var watcher in watchers)
            {
                watcher.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }
}
