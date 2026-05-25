using CodexWidget.Core;

namespace CodexWidget.App.Tests;

public sealed class WidgetPreferenceCoordinatorTests
{
    [Fact]
    public void CreateDraft_UsesCurrentPreferencesLoadedFromDefaults()
    {
        var defaults = WidgetPreferenceDefaults.Create();
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            defaults,
            _ => { });

        var draft = coordinator.CreateDraft();

        Assert.Equal(defaults.SelectedView, draft.SelectedView);
        Assert.Equal(defaults.CompactAccountLayout, draft.CompactAccountLayout);
        Assert.Equal(defaults.WidgetScalePercent, draft.WidgetScalePercent);
        Assert.Equal(defaults.AlwaysOnTop, draft.AlwaysOnTop);
        Assert.Equal(defaults.RefreshPeriodSeconds, draft.RefreshPeriodSeconds);
        Assert.Equal(defaults.Theme, draft.Theme);
    }

    [Fact]
    public void CreateDraft_PreservesCompactAccountLayoutFromCurrentPreferences()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var currentPreferences = WidgetPreferenceDefaults.Create() with
        {
            CompactAccountLayout = CompactAccountLayout.Horizontal,
        };
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            currentPreferences,
            _ => { });

        var draft = coordinator.CreateDraft();

        Assert.Equal(CompactAccountLayout.Horizontal, draft.CompactAccountLayout);
    }

    [Fact]
    public void CreateDraft_PreservesWidgetScaleFromCurrentPreferences()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var currentPreferences = WidgetPreferenceDefaults.Create() with
        {
            WidgetScalePercent = 130,
        };
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            currentPreferences,
            _ => { });

        var draft = coordinator.CreateDraft();

        Assert.Equal(130, draft.WidgetScalePercent);
    }

    [Fact]
    public void SaveAndApply_PreservesCompactLayoutAndNormalizesFullSelectedView()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var applied = new List<WidgetPreferences>();
        var currentPreferences = WidgetPreferenceDefaults.Create() with
        {
            CompactAccountLayout = CompactAccountLayout.Horizontal,
        };
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            currentPreferences,
            applied.Add);

        var draft = coordinator.CreateDraft();
        Assert.Equal(CompactAccountLayout.Horizontal, draft.CompactAccountLayout);

        var outcome = coordinator.SaveAndApply(draft with
        {
            SelectedView = WidgetViewKind.Full,
            AlwaysOnTop = false,
        });

        Assert.True(outcome.Succeeded);
        var appliedPreferences = Assert.Single(applied);
        Assert.Equal(WidgetViewKind.Compact, appliedPreferences.SelectedView);
        Assert.Equal(CompactAccountLayout.Horizontal, appliedPreferences.CompactAccountLayout);
        Assert.Equal(currentPreferences.WidgetScalePercent, appliedPreferences.WidgetScalePercent);

        var reloaded = store.Load();
        Assert.True(reloaded.Availability.IsAvailable);
        Assert.Equal(WidgetViewKind.Compact, reloaded.Preferences.SelectedView);
        Assert.Equal(CompactAccountLayout.Horizontal, reloaded.Preferences.CompactAccountLayout);
    }

    [Fact]
    public void SaveAndApply_NormalizesRefreshPeriodWithinBounds()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var applied = new List<WidgetPreferences>();
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            WidgetPreferenceDefaults.Create(),
            applied.Add);

        var outcome = coordinator.SaveAndApply(new WidgetPreferenceDraft
        {
            SelectedView = WidgetViewKind.Compact,
            CompactAccountLayout = CompactAccountLayout.Horizontal,
            WidgetScalePercent = 135,
            AlwaysOnTop = true,
            RefreshPeriodSeconds = 1,
        });

        Assert.True(outcome.Succeeded);
        Assert.Single(applied);
        Assert.Equal(WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds, applied[0].RefreshPeriodSeconds);
        Assert.Equal(140, applied[0].WidgetScalePercent);
        Assert.Equal(CompactAccountLayout.Horizontal, applied[0].CompactAccountLayout);
        Assert.Contains(outcome.Messages, message => message.Contains("normalized", StringComparison.OrdinalIgnoreCase));

        var reloaded = store.Load();
        Assert.True(reloaded.Availability.IsAvailable);
        Assert.Equal(WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds, reloaded.Preferences.RefreshPeriodSeconds);
        Assert.Equal(CompactAccountLayout.Horizontal, reloaded.Preferences.CompactAccountLayout);
    }

    [Fact]
    public void SaveAndApply_NormalizesInvalidSelectedViewAndAppliesTopmostState()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var applied = new List<WidgetPreferences>();
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            WidgetPreferenceDefaults.Create(),
            applied.Add);

        var outcome = coordinator.SaveAndApply(new WidgetPreferenceDraft
        {
            SelectedView = (WidgetViewKind)999,
            CompactAccountLayout = (CompactAccountLayout)999,
            WidgetScalePercent = 151,
            AlwaysOnTop = false,
            RefreshPeriodSeconds = 1,
            Theme = (WidgetThemePreference)999,
        });

        Assert.True(outcome.Succeeded);
        var appliedPreferences = Assert.Single(applied);
        Assert.Equal(WidgetViewKind.Compact, appliedPreferences.SelectedView);
        Assert.Equal(CompactAccountLayout.Vertical, appliedPreferences.CompactAccountLayout);
        Assert.Equal(WidgetPreferenceDefaults.MaximumWidgetScalePercent, appliedPreferences.WidgetScalePercent);
        Assert.False(appliedPreferences.AlwaysOnTop);
        Assert.Equal(WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds, appliedPreferences.RefreshPeriodSeconds);
        Assert.Equal(WidgetThemePreference.Light, appliedPreferences.Theme);
        Assert.Contains(outcome.Messages, message => message.Contains("normalized", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaveAndApply_SaveFailure_ReturnsRedactedDiagnosticsAndSkipsApply()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        var store = new PreferenceStore(
            new FixedPreferencePathProvider(filePath),
            new ExceptionInjectingPreferenceFileSystem(writeException: new IOException("Authorization: Bearer DemoSecretToken123")));
        var applyCallCount = 0;
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            WidgetPreferenceDefaults.Create(),
            _ => applyCallCount++);

        var outcome = coordinator.SaveAndApply(new WidgetPreferenceDraft
        {
            SelectedView = WidgetViewKind.Minimal,
            CompactAccountLayout = CompactAccountLayout.Vertical,
            WidgetScalePercent = 100,
            AlwaysOnTop = true,
            RefreshPeriodSeconds = 300,
        });

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, applyCallCount);
        Assert.NotEmpty(outcome.Messages);
        Assert.DoesNotContain("DemoSecretToken123", string.Join(" ", outcome.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveAndApply_ApplyFailure_ReturnsRedactedDiagnosticsAndSkipsApply()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            WidgetPreferenceDefaults.Create(),
            _ => throw new InvalidOperationException("Failed to apply settings for /home/demo/.codex/auth.json"));

        var outcome = coordinator.SaveAndApply(new WidgetPreferenceDraft
        {
            SelectedView = WidgetViewKind.Minimal,
            CompactAccountLayout = CompactAccountLayout.Vertical,
            WidgetScalePercent = 100,
            AlwaysOnTop = true,
            RefreshPeriodSeconds = 300,
        });

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.AppliedPreferences);
        Assert.DoesNotContain("/home/demo", string.Join(" ", outcome.Messages), StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", string.Join(" ", outcome.Messages), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DemoSecretToken", string.Join(" ", outcome.Messages), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveAndApply_ImmediatelyAppliesVisibleViewNormalizationAndTopmostState()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var applied = new List<WidgetPreferences>();
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            WidgetPreferenceDefaults.Create(),
            applied.Add);

        var outcome = coordinator.SaveAndApply(new WidgetPreferenceDraft
        {
            SelectedView = WidgetViewKind.Full,
            CompactAccountLayout = CompactAccountLayout.Horizontal,
            WidgetScalePercent = 120,
            AlwaysOnTop = false,
            RefreshPeriodSeconds = 300,
            Theme = WidgetThemePreference.Dark,
        });

        Assert.True(outcome.Succeeded);
        var appliedPreferences = Assert.Single(applied);
        Assert.Equal(WidgetViewKind.Compact, appliedPreferences.SelectedView);
        Assert.Equal(CompactAccountLayout.Horizontal, appliedPreferences.CompactAccountLayout);
        Assert.Equal(120, appliedPreferences.WidgetScalePercent);
        Assert.False(appliedPreferences.AlwaysOnTop);
        Assert.Equal(WidgetThemePreference.Dark, appliedPreferences.Theme);

        var reloaded = store.Load();
        Assert.True(reloaded.Availability.IsAvailable);
        Assert.Equal(WidgetThemePreference.Dark, reloaded.Preferences.Theme);
    }

    [Fact]
    public void ToggleCompactLayoutAndApply_PersistsToggledLayoutAndNormalizesFullSelectedView()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var applied = new List<WidgetPreferences>();
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            WidgetPreferenceDefaults.Create() with
            {
                CompactAccountLayout = CompactAccountLayout.Vertical,
            },
            applied.Add);

        var outcome = coordinator.ToggleCompactLayoutAndApply(WidgetViewKind.Full);

        Assert.True(outcome.Succeeded);
        var appliedPreferences = Assert.Single(applied);
        Assert.Equal(WidgetViewKind.Compact, appliedPreferences.SelectedView);
        Assert.Equal(CompactAccountLayout.Horizontal, appliedPreferences.CompactAccountLayout);

        var reloaded = store.Load();
        Assert.True(reloaded.Availability.IsAvailable);
        Assert.Equal(WidgetViewKind.Compact, reloaded.Preferences.SelectedView);
        Assert.Equal(CompactAccountLayout.Horizontal, reloaded.Preferences.CompactAccountLayout);
    }

    [Fact]
    public void AdjustWidgetScaleAndApply_PersistsSteppedScaleAndPreservesVisibleView()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var applied = new List<WidgetPreferences>();
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            WidgetPreferenceDefaults.Create() with
            {
                SelectedView = WidgetViewKind.Compact,
                WidgetScalePercent = 120,
            },
            applied.Add);

        var outcome = coordinator.AdjustWidgetScaleAndApply(WidgetViewKind.Minimal, 10);

        Assert.True(outcome.Succeeded);
        var appliedPreferences = Assert.Single(applied);
        Assert.Equal(WidgetViewKind.Minimal, appliedPreferences.SelectedView);
        Assert.Equal(130, appliedPreferences.WidgetScalePercent);

        var reloaded = store.Load();
        Assert.True(reloaded.Availability.IsAvailable);
        Assert.Equal(WidgetViewKind.Minimal, reloaded.Preferences.SelectedView);
        Assert.Equal(130, reloaded.Preferences.WidgetScalePercent);
    }

    [Fact]
    public void AdjustWidgetScaleAndApply_ClampsAtBounds()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var applied = new List<WidgetPreferences>();
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            WidgetPreferenceDefaults.Create() with
            {
                WidgetScalePercent = WidgetPreferenceDefaults.MaximumWidgetScalePercent,
            },
            applied.Add);

        var outcome = coordinator.AdjustWidgetScaleAndApply(WidgetViewKind.Compact, 10);

        Assert.True(outcome.Succeeded);
        Assert.Equal(WidgetPreferenceDefaults.MaximumWidgetScalePercent, Assert.Single(applied).WidgetScalePercent);
    }

    [Fact]
    public void ToggleCompactLayoutAndApply_InvalidStoredLayout_NormalizesBeforeToggle()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        WidgetPreferences? appliedPreferences = null;
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            WidgetPreferenceDefaults.Create() with
            {
                CompactAccountLayout = (CompactAccountLayout)999,
            },
            preferences => appliedPreferences = preferences);

        var outcome = coordinator.ToggleCompactLayoutAndApply(WidgetViewKind.Minimal);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(appliedPreferences);
        Assert.Equal(WidgetViewKind.Minimal, appliedPreferences!.SelectedView);
        Assert.Equal(CompactAccountLayout.Horizontal, appliedPreferences.CompactAccountLayout);
    }

    [Fact]
    public void ToggleCompactLayoutAndApply_ApplyFailure_ReturnsSavedPreferencesAndFailure()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var coordinator = new WidgetPreferenceCoordinator(
            store,
            WidgetPreferenceDefaults.Create() with
            {
                CompactAccountLayout = CompactAccountLayout.Horizontal,
            },
            _ => throw new InvalidOperationException("apply failed"));

        var outcome = coordinator.ToggleCompactLayoutAndApply(WidgetViewKind.Compact);

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.AppliedPreferences);
        Assert.Equal(CompactAccountLayout.Vertical, outcome.AppliedPreferences!.CompactAccountLayout);
        Assert.Contains(outcome.Messages, message => message.Contains("saved", StringComparison.OrdinalIgnoreCase));

        var reloaded = store.Load();
        Assert.True(reloaded.Availability.IsAvailable);
        Assert.Equal(CompactAccountLayout.Vertical, reloaded.Preferences.CompactAccountLayout);
    }

    private static PreferenceStore CreateStore(string directoryPath)
    {
        var filePath = Path.Combine(directoryPath, "settings.json");
        return new PreferenceStore(new FixedPreferencePathProvider(filePath));
    }

    private sealed class FixedPreferencePathProvider(string filePath) : IPreferencePathProvider
    {
        public string GetPreferenceFilePath() => filePath;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"CodexWidget.App.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class ExceptionInjectingPreferenceFileSystem(IOException? writeException) : IPreferenceFileSystem
    {
        private readonly SystemPreferenceFileSystem _inner = new();

        public bool FileExists(string path) => _inner.FileExists(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public string ReadAllText(string path) => _inner.ReadAllText(path);

        public Stream CreateWriteStream(string path)
        {
            if (writeException is not null)
            {
                throw writeException;
            }

            return _inner.CreateWriteStream(path);
        }

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            if (writeException is not null)
            {
                throw writeException;
            }

            _inner.MoveFile(sourcePath, destinationPath, overwrite);
        }

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern)
            => _inner.EnumerateFiles(directoryPath, searchPattern);

        public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);
    }
}
