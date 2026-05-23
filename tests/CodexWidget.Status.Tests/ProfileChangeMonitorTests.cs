using CodexWidget.Core;
using CodexWidget.Profiles;
using CodexWidget.Status;

namespace CodexWidget.Status.Tests;

public sealed class ProfileChangeMonitorTests : IDisposable
{
    private readonly List<string> temporaryDirectories = [];

    [Fact]
    public async Task WatcherEvents_AreDebouncedAndCoalesced()
    {
        var paths = CreateTempPaths();
        var watcherFactory = new ManualWatcherFactory();
        var delay = new ControlledDelay();
        using var monitor = new ProfileChangeMonitor(
            new ProfileChangeMonitorOptions
            {
                DebouncePeriod = TimeSpan.FromSeconds(1),
                PollingInterval = TimeSpan.FromSeconds(5),
            },
            watcherFactory,
            SystemProfileFileSystem.Instance,
            delay);
        var observed = new List<ProfileChangeSet>();
        monitor.Changed += (_, args) => observed.Add(args);

        monitor.UpdatePaths(paths);
        monitor.Start();
        Assert.NotNull(watcherFactory.Watcher);

        watcherFactory.Watcher!.RaiseChange(paths.CurrentAuthPath!);
        watcherFactory.Watcher.RaiseChange(paths.ConfigPath!);
        await WaitForAsync(() => delay.PendingCount > 0);

        Assert.Empty(observed);
        await PumpDelayUntilAsync(delay, () => observed.Count > 0);

        var change = Assert.Single(observed);
        Assert.Equal(StatusRefreshReason.ConfigChanged, change.Reason);
        Assert.Equal(StatusRefreshScope.Full, change.Scope);
        Assert.Contains(paths.CurrentAuthPath!, change.ChangedPaths);
        Assert.Contains(paths.ConfigPath!, change.ChangedPaths);
    }

    [Fact]
    public async Task PollingFallback_DetectsProfileMetadataChanges()
    {
        var paths = CreateTempPaths();
        var watcherFactory = new ManualWatcherFactory();
        var delay = new ControlledDelay();
        using var monitor = new ProfileChangeMonitor(
            new ProfileChangeMonitorOptions
            {
                DebouncePeriod = TimeSpan.FromSeconds(1),
                PollingInterval = TimeSpan.FromSeconds(5),
            },
            watcherFactory,
            SystemProfileFileSystem.Instance,
            delay);
        var observed = new List<ProfileChangeSet>();
        monitor.Changed += (_, args) => observed.Add(args);

        monitor.UpdatePaths(paths);
        monitor.Start();
        Assert.NotNull(watcherFactory.Watcher);
        watcherFactory.Watcher!.RaiseError(new IOException("Synthetic watcher failure."));
        await WaitForAsync(() => delay.PendingCount > 0);

        var savedProfilePath = Path.Combine(paths.ProfilesDirectory!, "work.json");
        await File.WriteAllTextAsync(savedProfilePath, "{ \"tokens\": { \"account_id\": \"changed-profile-account-id\" } }");
        File.SetLastWriteTimeUtc(savedProfilePath, DateTime.UtcNow.AddSeconds(2));
        monitor.PollForChangesOnceForTesting();

        await PumpDelayUntilAsync(delay, () => observed.Count > 0);
        var change = Assert.Single(observed);
        Assert.Equal(StatusRefreshReason.ProfileChanged, change.Reason);
        Assert.Contains(savedProfilePath, change.ChangedPaths);
    }

    [Fact]
    public async Task PollingFallback_NoMetadataChanges_DoesNotEmit()
    {
        var paths = CreateTempPaths();
        var delay = new ControlledDelay();
        using var monitor = new ProfileChangeMonitor(
            new ProfileChangeMonitorOptions
            {
                DebouncePeriod = TimeSpan.FromSeconds(1),
                PollingInterval = TimeSpan.FromSeconds(5),
            },
            new UnavailableWatcherFactory(),
            SystemProfileFileSystem.Instance,
            delay);
        var observed = new List<ProfileChangeSet>();
        monitor.Changed += (_, args) => observed.Add(args);

        monitor.UpdatePaths(paths);
        monitor.Start();

        delay.ReleaseNext();
        await Task.Yield();

        Assert.Empty(observed);
    }

    public void Dispose()
    {
        foreach (var directory in temporaryDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private ProfileChangeMonitorPaths CreateTempPaths()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"codex-widget-status-tests-{Guid.NewGuid():N}");
        temporaryDirectories.Add(tempRoot);

        var codexDirectory = Path.Combine(tempRoot, ".codex");
        var profilesDirectory = Path.Combine(codexDirectory, "profiles");
        Directory.CreateDirectory(profilesDirectory);

        var authPath = Path.Combine(codexDirectory, "auth.json");
        var configPath = Path.Combine(codexDirectory, "config.toml");
        var profilesIndexPath = Path.Combine(profilesDirectory, "profiles.json");
        var savedProfilePath = Path.Combine(profilesDirectory, "work.json");
        var excludedUpdatePath = Path.Combine(profilesDirectory, "update.json");

        File.WriteAllText(authPath, "{ \"tokens\": { \"account_id\": \"acct-1\" } }");
        File.WriteAllText(configPath, "chatgpt_base_url = \"https://chatgpt.com/backend-api\"\n");
        File.WriteAllText(profilesIndexPath, "{ \"profiles\": { \"work\": { \"label\": \"Work\" } } }");
        File.WriteAllText(savedProfilePath, "{ \"tokens\": { \"account_id\": \"acct-work\" } }");
        File.WriteAllText(excludedUpdatePath, "{ \"meta\": true }");

        return ProfileChangeMonitorPaths.FromCodexHomePaths(new CodexHomePaths
        {
            HomeDirectory = tempRoot,
            CodexDirectory = codexDirectory,
            CurrentAuthPath = authPath,
            ProfilesDirectory = profilesDirectory,
            ProfilesIndexPath = profilesIndexPath,
            ProfilesLockPath = Path.Combine(profilesDirectory, "profiles.lock"),
            ConfigPath = configPath,
        });
    }

    private static async Task PumpDelayUntilAsync(ControlledDelay delay, Func<bool> predicate)
    {
        for (var index = 0; index < 50 && !predicate(); index++)
        {
            if (delay.PendingCount == 0)
            {
                await Task.Delay(10);
                continue;
            }

            delay.ReleaseNext();
            await Task.Delay(10);
        }

        Assert.True(predicate(), "Expected delayed background work to complete.");
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        for (var index = 0; index < 30; index++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(predicate(), "Condition was not met within timeout.");
    }

    private sealed class ControlledDelay : IAsyncDelay
    {
        private readonly Queue<TaskCompletionSource<object?>> pending = new();
        private readonly object sync = new();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            }

            lock (sync)
            {
                pending.Enqueue(tcs);
            }

            return tcs.Task;
        }

        public int PendingCount
        {
            get
            {
                lock (sync)
                {
                    return pending.Count;
                }
            }
        }

        public void ReleaseNext()
        {
            TaskCompletionSource<object?>? next = null;
            lock (sync)
            {
                if (pending.Count > 0)
                {
                    next = pending.Dequeue();
                }
            }

            next?.TrySetResult(null);
        }
    }

    private sealed class ManualWatcherFactory : IProfileFileWatcherFactory
    {
        public ManualWatcher? Watcher { get; private set; }

        public IProfileFileWatcher? TryCreate(ProfileChangeMonitorPaths paths, Action<string> onChanged, Action<Exception> onError)
        {
            Watcher = new ManualWatcher(onChanged, onError);
            return Watcher;
        }
    }

    private sealed class UnavailableWatcherFactory : IProfileFileWatcherFactory
    {
        public IProfileFileWatcher? TryCreate(ProfileChangeMonitorPaths paths, Action<string> onChanged, Action<Exception> onError)
        {
            return null;
        }
    }

    private sealed class ManualWatcher(Action<string> onChanged, Action<Exception> onError) : IProfileFileWatcher
    {
        public void Start()
        {
        }

        public void RaiseChange(string path)
        {
            onChanged(path);
        }

        public void RaiseError(Exception exception)
        {
            onError(exception);
        }

        public void Dispose()
        {
        }
    }
}
