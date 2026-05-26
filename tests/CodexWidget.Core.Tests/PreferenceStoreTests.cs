namespace CodexWidget.Core.Tests;

public sealed class PreferenceStoreTests
{
    [Fact]
    public void Defaults_MatchDesignRequirements()
    {
        var defaults = WidgetPreferenceDefaults.Create();

        Assert.Equal(WidgetPreferenceDefaults.CurrentSchemaVersion, defaults.SchemaVersion);
        Assert.Equal(WidgetViewKind.Compact, defaults.SelectedView);
        Assert.Equal(CompactAccountLayout.Vertical, defaults.CompactAccountLayout);
        Assert.Equal(100, defaults.WidgetScalePercent);
        Assert.Equal(100, WidgetPreferenceDefaults.MinimumWidgetScalePercent);
        Assert.Equal(150, WidgetPreferenceDefaults.MaximumWidgetScalePercent);
        Assert.Equal(10, WidgetPreferenceDefaults.WidgetScaleStepPercent);
        Assert.True(defaults.AlwaysOnTop);
        Assert.Equal(300, defaults.RefreshPeriodSeconds);
        Assert.Equal(60, WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds);
        Assert.Equal(24 * 60 * 60, WidgetPreferenceDefaults.MaximumRefreshPeriodSeconds);
        Assert.Equal(WidgetThemePreference.Light, defaults.Theme);
        Assert.Equal(56, UsageConfigurationRules.GetTotalWeeklyHours(defaults.WorkSchedule));
        Assert.Equal(70, defaults.QuotaThresholds.RedBelowPercent);
        Assert.Equal(90, defaults.QuotaThresholds.YellowBelowPercent);
        Assert.Equal(110, defaults.QuotaThresholds.BlueAbovePercent);
        Assert.Equal(130, defaults.QuotaThresholds.PinkAbovePercent);
    }

    [Fact]
    public void Load_MissingFile_UsesDefaultsWithoutFailure()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.True(result.Availability.IsAvailable);
        Assert.True(result.UsedDefaults);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(WidgetPreferenceDefaults.Create(), result.Preferences);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PersistsOnlyPreferenceFields()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "nested", "settings.json");
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));
        var input = new WidgetPreferences
        {
            SchemaVersion = WidgetPreferenceDefaults.CurrentSchemaVersion,
            SelectedView = WidgetViewKind.Full,
            CompactAccountLayout = CompactAccountLayout.Horizontal,
            WidgetScalePercent = 130,
            AlwaysOnTop = false,
            RefreshPeriodSeconds = 601,
            Theme = WidgetThemePreference.Dark,
            WindowPlacement = new WindowPlacementPreferences
            {
                X = 15,
                Y = 30,
                Width = 800,
                Height = 600,
                ScreenKey = "DISPLAY1",
            },
        };

        var saveResult = store.Save(input);
        Assert.True(saveResult.Availability.IsAvailable);

        var reload = store.Load();
        Assert.True(reload.Availability.IsAvailable);
        Assert.False(reload.UsedDefaults);
        Assert.Equal(input, reload.Preferences);

        var json = File.ReadAllText(filePath);
        Assert.Contains("\"schemaVersion\"", json, StringComparison.Ordinal);
        Assert.Contains("\"selectedView\"", json, StringComparison.Ordinal);
        Assert.Contains("\"compactAccountLayout\"", json, StringComparison.Ordinal);
        Assert.Contains("\"widgetScalePercent\"", json, StringComparison.Ordinal);
        Assert.Contains("\"alwaysOnTop\"", json, StringComparison.Ordinal);
        Assert.Contains("\"refreshPeriodSeconds\"", json, StringComparison.Ordinal);
        Assert.Contains("\"theme\"", json, StringComparison.Ordinal);
        Assert.Contains("\"workSchedule\"", json, StringComparison.Ordinal);
        Assert.Contains("\"quotaThresholds\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"warningThresholds\"", json, StringComparison.Ordinal);
        Assert.Contains("\"windowPlacement\"", json, StringComparison.Ordinal);

        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profileSnapshot", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_SerializesCompactAccountLayoutField()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));
        var preferences = WidgetPreferenceDefaults.Create() with
        {
            CompactAccountLayout = CompactAccountLayout.Horizontal,
        };

        var saveResult = store.Save(preferences);

        Assert.True(saveResult.Availability.IsAvailable);
        var json = File.ReadAllText(filePath);
        Assert.Contains("\"compactAccountLayout\": \"Horizontal\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_SerializesWidgetScalePercentField()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));
        var preferences = WidgetPreferenceDefaults.Create() with
        {
            WidgetScalePercent = 140,
        };

        var saveResult = store.Save(preferences);

        Assert.True(saveResult.Availability.IsAvailable);
        var json = File.ReadAllText(filePath);
        Assert.Contains("\"widgetScalePercent\": 140", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_RejectsInvalidCompactAccountLayoutValue()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));
        var preferences = WidgetPreferenceDefaults.Create() with
        {
            CompactAccountLayout = (CompactAccountLayout)999,
        };

        var saveResult = store.Save(preferences);

        Assert.False(saveResult.Availability.IsAvailable);
        Assert.Contains(saveResult.Diagnostics, diagnostic => diagnostic.Summary.Contains("compactAccountLayout", StringComparison.Ordinal));
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void Save_SerializesThemeField()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));
        var preferences = WidgetPreferenceDefaults.Create() with
        {
            Theme = WidgetThemePreference.Dark,
        };

        var saveResult = store.Save(preferences);

        Assert.True(saveResult.Availability.IsAvailable);
        var json = File.ReadAllText(filePath);
        Assert.Contains("\"theme\": \"Dark\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Schema1_MigratesToCurrentSchemaWithVerticalCompactLayoutAndDefaultScale()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 1,
              "selectedView": "Compact",
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 300,
              "windowPlacement": {
                "x": 0,
                "y": 0,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.True(result.Availability.IsAvailable);
        Assert.False(result.UsedDefaults);
        Assert.Equal(WidgetPreferenceDefaults.CurrentSchemaVersion, result.Preferences.SchemaVersion);
        Assert.Equal(CompactAccountLayout.Vertical, result.Preferences.CompactAccountLayout);
        Assert.Equal(WidgetPreferenceDefaults.DefaultWidgetScalePercent, result.Preferences.WidgetScalePercent);
        Assert.Equal(WidgetThemePreference.Light, result.Preferences.Theme);
    }

    [Fact]
    public void Load_Schema2_MigratesToCurrentSchemaWithDefaultScale()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 2,
              "selectedView": "Compact",
              "compactAccountLayout": "Horizontal",
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 300,
              "windowPlacement": {
                "x": 0,
                "y": 0,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.True(result.Availability.IsAvailable);
        Assert.False(result.UsedDefaults);
        Assert.Equal(WidgetPreferenceDefaults.CurrentSchemaVersion, result.Preferences.SchemaVersion);
        Assert.Equal(CompactAccountLayout.Horizontal, result.Preferences.CompactAccountLayout);
        Assert.Equal(WidgetPreferenceDefaults.DefaultWidgetScalePercent, result.Preferences.WidgetScalePercent);
        Assert.Equal(WidgetThemePreference.Light, result.Preferences.Theme);
    }

    [Fact]
    public void Load_Schema4_MigratesToCurrentSchemaWithDefaultTheme()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 4,
              "selectedView": "Compact",
              "compactAccountLayout": "Horizontal",
              "widgetScalePercent": 140,
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 300,
              "windowPlacement": {
                "x": 0,
                "y": 0,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.True(result.Availability.IsAvailable);
        Assert.False(result.UsedDefaults);
        Assert.Equal(WidgetPreferenceDefaults.CurrentSchemaVersion, result.Preferences.SchemaVersion);
        Assert.Equal(WidgetThemePreference.Light, result.Preferences.Theme);
        Assert.Equal(56, UsageConfigurationRules.GetTotalWeeklyHours(result.Preferences.WorkSchedule));
        Assert.Equal(70, result.Preferences.QuotaThresholds.RedBelowPercent);
    }

    [Fact]
    public void Load_RejectsMalformedSchemaVersion()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 0,
              "selectedView": "Compact",
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 300,
              "windowPlacement": {
                "x": 0,
                "y": 0,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.False(result.Availability.IsAvailable);
        Assert.True(result.UsedDefaults);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is SourceDiagnosticCode.Malformed);
    }

    [Fact]
    public void Load_RejectsMissingRequiredFields()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 1,
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 300,
              "windowPlacement": {
                "x": 0,
                "y": 0,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.False(result.Availability.IsAvailable);
        Assert.True(result.UsedDefaults);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.MissingRequiredField);
    }

    [Fact]
    public void Load_RejectsUnknownSelectedView()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 1,
              "selectedView": "SuperDense",
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 300,
              "windowPlacement": {
                "x": 0,
                "y": 0,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.False(result.Availability.IsAvailable);
        Assert.True(result.UsedDefaults);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Summary.Contains("selectedView", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_RejectsMalformedCompactAccountLayoutForSchema2()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 2,
              "selectedView": "Compact",
              "compactAccountLayout": "Diagonal",
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 300,
              "windowPlacement": {
                "x": 0,
                "y": 0,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.False(result.Availability.IsAvailable);
        Assert.True(result.UsedDefaults);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Summary.Contains("compactAccountLayout", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_RejectsMalformedThemeForSchema6()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 6,
              "selectedView": "Compact",
              "compactAccountLayout": "Horizontal",
              "widgetScalePercent": 140,
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 300,
              "theme": "Solarized",
              "workSchedule": {
                "monday": [{ "start": "07:00", "end": "17:00" }],
                "tuesday": [{ "start": "07:00", "end": "17:00" }],
                "wednesday": [{ "start": "07:00", "end": "17:00" }],
                "thursday": [{ "start": "07:00", "end": "17:00" }],
                "friday": [{ "start": "07:00", "end": "17:00" }],
                "saturday": [{ "start": "20:00", "end": "23:00" }],
                "sunday": [{ "start": "20:00", "end": "23:00" }]
              },
              "quotaThresholds": {
                "redBelowPercent": 70,
                "yellowBelowPercent": 90,
                "blueAbovePercent": 110,
                "pinkAbovePercent": 130
              },
              "windowPlacement": {
                "x": 0,
                "y": 0,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.False(result.Availability.IsAvailable);
        Assert.True(result.UsedDefaults);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Summary.Contains("theme", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_RejectsOverlappingWorkScheduleForCurrentSchema()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 6,
              "selectedView": "Compact",
              "compactAccountLayout": "Horizontal",
              "widgetScalePercent": 140,
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 300,
              "theme": "Light",
              "workSchedule": {
                "monday": [
                  { "start": "09:00", "end": "11:00" },
                  { "start": "10:30", "end": "12:00" }
                ],
                "tuesday": [],
                "wednesday": [],
                "thursday": [],
                "friday": [],
                "saturday": [],
                "sunday": []
              },
              "quotaThresholds": {
                "redBelowPercent": 70,
                "yellowBelowPercent": 90,
                "blueAbovePercent": 110,
                "pinkAbovePercent": 130
              },
              "windowPlacement": {
                "x": 0,
                "y": 0,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.False(result.Availability.IsAvailable);
        Assert.True(result.UsedDefaults);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Summary.Contains("workSchedule monday[1]", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_ClampsRefreshPeriodAndScaleWithinBounds()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 1,
              "selectedView": "Compact",
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 10,
              "windowPlacement": {
                "x": 1,
                "y": 2,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.True(result.Availability.IsAvailable);
        Assert.False(result.UsedDefaults);
        Assert.Equal(WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds, result.Preferences.RefreshPeriodSeconds);
        Assert.Equal(WidgetPreferenceDefaults.DefaultWidgetScalePercent, result.Preferences.WidgetScalePercent);
    }

    [Fact]
    public void Load_ClampsAndRoundsWidgetScaleWithinBounds()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 3,
              "selectedView": "Compact",
              "compactAccountLayout": "Vertical",
              "widgetScalePercent": 136,
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 300,
              "windowPlacement": {
                "x": 1,
                "y": 2,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.True(result.Availability.IsAvailable);
        Assert.False(result.UsedDefaults);
        Assert.Equal(140, result.Preferences.WidgetScalePercent);
    }

    [Fact]
    public void Load_RejectsInvalidPlacementSizes()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 1,
              "selectedView": "Compact",
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 120,
              "windowPlacement": {
                "x": 1,
                "y": 2,
                "width": 0,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.False(result.Availability.IsAvailable);
        Assert.True(result.UsedDefaults);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Summary.Contains("width", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Load_RejectsNewerSchemaVersionWithoutCrashing()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, """
            {
              "schemaVersion": 99,
              "selectedView": "Compact",
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 300,
              "windowPlacement": {
                "x": 0,
                "y": 0,
                "width": 360,
                "height": 240
              }
            }
            """);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.False(result.Availability.IsAvailable);
        Assert.True(result.UsedDefaults);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Unavailable);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsSafeUnavailableResult()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, "{ malformed ");
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));

        var result = store.Load();

        Assert.False(result.Availability.IsAvailable);
        Assert.True(result.UsedDefaults);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Malformed);
    }

    [Fact]
    public void Save_UsesAtomicMoveAndReadAfterWrite()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));
        var first = WidgetPreferenceDefaults.Create();
        var second = first with
        {
            RefreshPeriodSeconds = 7200,
            SelectedView = WidgetViewKind.Minimal,
            WindowPlacement = first.WindowPlacement with { Width = 444, Height = 222 },
        };

        Assert.True(store.Save(first).Availability.IsAvailable);
        Assert.True(store.Save(second).Availability.IsAvailable);

        var load = store.Load();
        Assert.True(load.Availability.IsAvailable);
        Assert.Equal(second, load.Preferences);

        var directoryFiles = Directory.GetFiles(Path.GetDirectoryName(filePath)!);
        Assert.DoesNotContain(directoryFiles, path => path.Contains(".tmp.", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_RecoversFromTemporaryFile_WhenPrimaryFileMissing()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        var temporaryPath = Path.Combine(directory.Path, "settings.json.tmp.recovery");
        WriteJson(temporaryPath, """
            {
              "schemaVersion": 1,
              "selectedView": "Full",
              "alwaysOnTop": true,
              "refreshPeriodSeconds": 600,
              "windowPlacement": {
                "x": 40,
                "y": 10,
                "width": 420,
                "height": 260
              }
            }
            """);

        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath));
        var result = store.Load();

        Assert.True(result.Availability.IsAvailable);
        Assert.False(result.UsedDefaults);
        Assert.Equal(WidgetViewKind.Full, result.Preferences.SelectedView);
        Assert.Equal(WidgetPreferenceDefaults.CurrentSchemaVersion, result.Preferences.SchemaVersion);
        Assert.Equal(CompactAccountLayout.Vertical, result.Preferences.CompactAccountLayout);
        Assert.True(File.Exists(filePath));
        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public void Load_IoFailure_ReturnsSafeUnavailableResult()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, "{}");

        var failingFileSystem = new FailingPreferenceFileSystem(throwOnRead: true, throwOnWrite: false);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath), failingFileSystem);

        var result = store.Load();

        Assert.False(result.Availability.IsAvailable);
        Assert.True(result.UsedDefaults);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Error);
    }

    [Fact]
    public void Save_IoFailure_ReturnsSafeUnavailableResult()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");

        var failingFileSystem = new FailingPreferenceFileSystem(throwOnRead: false, throwOnWrite: true);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath), failingFileSystem);

        var result = store.Save(WidgetPreferenceDefaults.Create());

        Assert.False(result.Availability.IsAvailable);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Error);
    }

    [Fact]
    public void Load_FailureDiagnostics_RedactDetailAndContext()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "settings.json");
        WriteJson(filePath, "{}");

        var failingFileSystem = new ExceptionInjectingPreferenceFileSystem(
            readException: new IOException("Could not open /home/example/.codex/auth.json."),
            writeException: null);
        var store = new PreferenceStore(new FixedPreferencePathProvider(filePath), failingFileSystem);

        var result = store.Load();
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(SourceDiagnosticCode.Error, diagnostic.Code);
        Assert.Equal("[redacted-path]/.codex/auth.json", diagnostic.Detail);
        Assert.StartsWith("[redacted-path]/", diagnostic.Context["preferenceFilePath"], StringComparison.Ordinal);
        Assert.EndsWith("/settings.json", diagnostic.Context["preferenceFilePath"], StringComparison.Ordinal);
        Assert.Equal("load", diagnostic.Context["operation"]);
        Assert.Equal("IOException", diagnostic.Context["exceptionType"]);
    }

    private static void WriteJson(string filePath, string json)
    {
        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        File.WriteAllText(filePath, json);
    }

    private sealed class FixedPreferencePathProvider(string filePath) : IPreferencePathProvider
    {
        public string GetPreferenceFilePath() => filePath;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"CodexWidget.Core.Tests.{Guid.NewGuid():N}");
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

    private sealed class FailingPreferenceFileSystem(bool throwOnRead, bool throwOnWrite) : IPreferenceFileSystem
    {
        private readonly SystemPreferenceFileSystem _inner = new();

        public bool FileExists(string path) => _inner.FileExists(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public string ReadAllText(string path)
        {
            if (throwOnRead)
            {
                throw new IOException("Injected read failure.");
            }

            return _inner.ReadAllText(path);
        }

        public Stream CreateWriteStream(string path)
        {
            if (throwOnWrite)
            {
                throw new IOException("Injected write failure.");
            }

            return _inner.CreateWriteStream(path);
        }

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            if (throwOnWrite)
            {
                throw new IOException("Injected write failure.");
            }

            _inner.MoveFile(sourcePath, destinationPath, overwrite);
        }

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern) => _inner.EnumerateFiles(directoryPath, searchPattern);

        public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);
    }

    private sealed class ExceptionInjectingPreferenceFileSystem(Exception? readException, Exception? writeException) : IPreferenceFileSystem
    {
        private readonly SystemPreferenceFileSystem _inner = new();

        public bool FileExists(string path) => _inner.FileExists(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public string ReadAllText(string path)
        {
            if (readException is not null)
            {
                throw readException;
            }

            return _inner.ReadAllText(path);
        }

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

        public IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern) => _inner.EnumerateFiles(directoryPath, searchPattern);

        public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);
    }
}
