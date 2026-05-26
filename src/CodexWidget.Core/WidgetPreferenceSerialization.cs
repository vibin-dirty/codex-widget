using System.Globalization;
using System.Text.Json.Serialization;

namespace CodexWidget.Core;

internal sealed class WidgetPreferencesDocument
{
    [JsonPropertyName("schemaVersion")]
    public int? SchemaVersion { get; init; }

    [JsonPropertyName("selectedView")]
    public string? SelectedView { get; init; }

    [JsonPropertyName("compactAccountLayout")]
    public string? CompactAccountLayout { get; init; }

    [JsonPropertyName("widgetScalePercent")]
    public int? WidgetScalePercent { get; init; }

    [JsonPropertyName("alwaysOnTop")]
    public bool? AlwaysOnTop { get; init; }

    [JsonPropertyName("refreshPeriodSeconds")]
    public int? RefreshPeriodSeconds { get; init; }

    [JsonPropertyName("theme")]
    public string? Theme { get; init; }

    [JsonPropertyName("workSchedule")]
    public WeeklyWorkScheduleDocument? WorkSchedule { get; init; }

    [JsonPropertyName("quotaThresholds")]
    public QuotaThresholdsDocument? QuotaThresholds { get; init; }

    [JsonPropertyName("windowPlacement")]
    public WindowPlacementPreferencesDocument? WindowPlacement { get; init; }

    public static WidgetPreferencesDocument FromPreferences(WidgetPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return new WidgetPreferencesDocument
        {
            SchemaVersion = preferences.SchemaVersion,
            SelectedView = preferences.SelectedView.ToString(),
            CompactAccountLayout = preferences.CompactAccountLayout.ToString(),
            WidgetScalePercent = preferences.WidgetScalePercent,
            AlwaysOnTop = preferences.AlwaysOnTop,
            RefreshPeriodSeconds = preferences.RefreshPeriodSeconds,
            Theme = preferences.Theme.ToString(),
            WorkSchedule = WeeklyWorkScheduleDocument.FromPreferences(preferences.WorkSchedule),
            QuotaThresholds = QuotaThresholdsDocument.FromPreferences(preferences.QuotaThresholds),
            WindowPlacement = new WindowPlacementPreferencesDocument
            {
                X = preferences.WindowPlacement.X,
                Y = preferences.WindowPlacement.Y,
                Width = preferences.WindowPlacement.Width,
                Height = preferences.WindowPlacement.Height,
                ScreenKey = preferences.WindowPlacement.ScreenKey,
            },
        };
    }
}

internal sealed class WindowPlacementPreferencesDocument
{
    [JsonPropertyName("x")]
    public int? X { get; init; }

    [JsonPropertyName("y")]
    public int? Y { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }

    [JsonPropertyName("screenKey")]
    public string? ScreenKey { get; init; }
}

internal sealed class WeeklyWorkScheduleDocument
{
    [JsonPropertyName("monday")]
    public WorkWindowDocument[]? Monday { get; init; }

    [JsonPropertyName("tuesday")]
    public WorkWindowDocument[]? Tuesday { get; init; }

    [JsonPropertyName("wednesday")]
    public WorkWindowDocument[]? Wednesday { get; init; }

    [JsonPropertyName("thursday")]
    public WorkWindowDocument[]? Thursday { get; init; }

    [JsonPropertyName("friday")]
    public WorkWindowDocument[]? Friday { get; init; }

    [JsonPropertyName("saturday")]
    public WorkWindowDocument[]? Saturday { get; init; }

    [JsonPropertyName("sunday")]
    public WorkWindowDocument[]? Sunday { get; init; }

    public static WeeklyWorkScheduleDocument FromPreferences(WeeklyWorkSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return new WeeklyWorkScheduleDocument
        {
            Monday = WorkWindowDocument.FromPreferences(schedule.Monday),
            Tuesday = WorkWindowDocument.FromPreferences(schedule.Tuesday),
            Wednesday = WorkWindowDocument.FromPreferences(schedule.Wednesday),
            Thursday = WorkWindowDocument.FromPreferences(schedule.Thursday),
            Friday = WorkWindowDocument.FromPreferences(schedule.Friday),
            Saturday = WorkWindowDocument.FromPreferences(schedule.Saturday),
            Sunday = WorkWindowDocument.FromPreferences(schedule.Sunday),
        };
    }
}

internal sealed class WorkWindowDocument
{
    [JsonPropertyName("start")]
    public string? Start { get; init; }

    [JsonPropertyName("end")]
    public string? End { get; init; }

    public static WorkWindowDocument[] FromPreferences(DayWorkSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return schedule.Windows
            .Select(window => new WorkWindowDocument
            {
                Start = window.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                End = window.End.ToString("HH:mm", CultureInfo.InvariantCulture),
            })
            .ToArray();
    }
}

internal sealed class QuotaThresholdsDocument
{
    [JsonPropertyName("redBelowPercent")]
    public int? RedBelowPercent { get; init; }

    [JsonPropertyName("yellowBelowPercent")]
    public int? YellowBelowPercent { get; init; }

    [JsonPropertyName("blueAbovePercent")]
    public int? BlueAbovePercent { get; init; }

    [JsonPropertyName("pinkAbovePercent")]
    public int? PinkAbovePercent { get; init; }

    public static QuotaThresholdsDocument FromPreferences(QuotaThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        return new QuotaThresholdsDocument
        {
            RedBelowPercent = thresholds.RedBelowPercent,
            YellowBelowPercent = thresholds.YellowBelowPercent,
            BlueAbovePercent = thresholds.BlueAbovePercent,
            PinkAbovePercent = thresholds.PinkAbovePercent,
        };
    }
}

internal sealed record WidgetPreferenceMigrationResult(
    WidgetPreferencesDocument? Document,
    SourceDiagnostic? Diagnostic)
{
    public bool Succeeded => Diagnostic is null && Document is not null;
}

internal static class WidgetPreferenceMigrator
{
    public static WidgetPreferenceMigrationResult MigrateToCurrent(WidgetPreferencesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.SchemaVersion.HasValue)
        {
            return new WidgetPreferenceMigrationResult(
                null,
                CreateDiagnostic(
                    SourceDiagnosticCode.MissingRequiredField,
                    "Preference schema version is missing."));
        }

        if (document.SchemaVersion.Value <= 0)
        {
            return new WidgetPreferenceMigrationResult(
                null,
                CreateDiagnostic(
                    SourceDiagnosticCode.Malformed,
                    "Preference schema version must be a positive integer."));
        }

        if (document.SchemaVersion.Value > WidgetPreferenceDefaults.CurrentSchemaVersion)
        {
            return new WidgetPreferenceMigrationResult(
                null,
                CreateDiagnostic(
                    SourceDiagnosticCode.Unavailable,
                    $"Preference schema version {document.SchemaVersion.Value} is newer than supported schema version {WidgetPreferenceDefaults.CurrentSchemaVersion}."));
        }

        return document.SchemaVersion.Value switch
        {
            1 => new WidgetPreferenceMigrationResult(MigrateSchema5ToSchema6(MigrateSchema4ToSchema5(MigrateSchema3ToSchema4(MigrateSchema2ToSchema3(MigrateSchema1ToSchema2(document))))), null),
            2 => new WidgetPreferenceMigrationResult(MigrateSchema5ToSchema6(MigrateSchema4ToSchema5(MigrateSchema3ToSchema4(MigrateSchema2ToSchema3(document)))), null),
            3 => new WidgetPreferenceMigrationResult(MigrateSchema5ToSchema6(MigrateSchema4ToSchema5(MigrateSchema3ToSchema4(document))), null),
            4 => new WidgetPreferenceMigrationResult(MigrateSchema5ToSchema6(MigrateSchema4ToSchema5(document)), null),
            5 => new WidgetPreferenceMigrationResult(MigrateSchema5ToSchema6(document), null),
            6 => new WidgetPreferenceMigrationResult(document, null),
            _ => new WidgetPreferenceMigrationResult(
                null,
                CreateDiagnostic(
                    SourceDiagnosticCode.Malformed,
                    $"Preference schema version {document.SchemaVersion.Value} is unsupported.")),
        };
    }

    private static WidgetPreferencesDocument MigrateSchema1ToSchema2(WidgetPreferencesDocument document)
    {
        return new WidgetPreferencesDocument
        {
            SchemaVersion = 2,
            SelectedView = document.SelectedView,
            CompactAccountLayout = WidgetPreferenceDefaults.DefaultCompactAccountLayout.ToString(),
            WidgetScalePercent = document.WidgetScalePercent,
            AlwaysOnTop = document.AlwaysOnTop,
            RefreshPeriodSeconds = document.RefreshPeriodSeconds,
            Theme = document.Theme,
            WindowPlacement = document.WindowPlacement,
        };
    }

    private static WidgetPreferencesDocument MigrateSchema2ToSchema3(WidgetPreferencesDocument document)
    {
        return new WidgetPreferencesDocument
        {
            SchemaVersion = 3,
            SelectedView = document.SelectedView,
            CompactAccountLayout = document.CompactAccountLayout,
            WidgetScalePercent = WidgetPreferenceDefaults.DefaultWidgetScalePercent,
            AlwaysOnTop = document.AlwaysOnTop,
            RefreshPeriodSeconds = document.RefreshPeriodSeconds,
            Theme = document.Theme,
            WindowPlacement = document.WindowPlacement,
        };
    }

    private static WidgetPreferencesDocument MigrateSchema3ToSchema4(WidgetPreferencesDocument document)
    {
        return new WidgetPreferencesDocument
        {
            SchemaVersion = 4,
            SelectedView = document.SelectedView,
            CompactAccountLayout = document.CompactAccountLayout,
            WidgetScalePercent = document.WidgetScalePercent,
            AlwaysOnTop = document.AlwaysOnTop,
            RefreshPeriodSeconds = document.RefreshPeriodSeconds,
            Theme = document.Theme,
            WindowPlacement = document.WindowPlacement,
        };
    }

    private static WidgetPreferencesDocument MigrateSchema4ToSchema5(WidgetPreferencesDocument document)
    {
        return new WidgetPreferencesDocument
        {
            SchemaVersion = 5,
            SelectedView = document.SelectedView,
            CompactAccountLayout = document.CompactAccountLayout,
            WidgetScalePercent = document.WidgetScalePercent,
            AlwaysOnTop = document.AlwaysOnTop,
            RefreshPeriodSeconds = document.RefreshPeriodSeconds,
            Theme = string.IsNullOrWhiteSpace(document.Theme)
                ? WidgetPreferenceDefaults.DefaultTheme.ToString()
                : document.Theme,
            WindowPlacement = document.WindowPlacement,
        };
    }

    private static WidgetPreferencesDocument MigrateSchema5ToSchema6(WidgetPreferencesDocument document)
    {
        return new WidgetPreferencesDocument
        {
            SchemaVersion = WidgetPreferenceDefaults.CurrentSchemaVersion,
            SelectedView = document.SelectedView,
            CompactAccountLayout = document.CompactAccountLayout,
            WidgetScalePercent = document.WidgetScalePercent,
            AlwaysOnTop = document.AlwaysOnTop,
            RefreshPeriodSeconds = document.RefreshPeriodSeconds,
            Theme = document.Theme,
            WorkSchedule = document.WorkSchedule ?? WeeklyWorkScheduleDocument.FromPreferences(UsageConfigurationDefaults.CreateDefaultWeeklyWorkSchedule()),
            QuotaThresholds = document.QuotaThresholds ?? QuotaThresholdsDocument.FromPreferences(UsageConfigurationDefaults.CreateDefaultQuotaThresholds()),
            WindowPlacement = document.WindowPlacement,
        };
    }

    private static SourceDiagnostic CreateDiagnostic(SourceDiagnosticCode code, string summary)
    {
        return new SourceDiagnostic
        {
            Code = code,
            Severity = SourceDiagnosticSeverity.Error,
            Summary = summary,
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}

internal sealed record WidgetPreferenceValidationResult(
    WidgetPreferences? Preferences,
    IReadOnlyList<SourceDiagnostic> Diagnostics)
{
    public bool Succeeded => Preferences is not null && Diagnostics.Count == 0;
}

internal static class WidgetPreferenceValidator
{
    public static WidgetPreferenceValidationResult ValidateAndNormalize(WidgetPreferencesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<SourceDiagnostic>();

        if (!document.SchemaVersion.HasValue)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference schema version is missing."));
        }
        else if (document.SchemaVersion.Value != WidgetPreferenceDefaults.CurrentSchemaVersion)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.Malformed, "Preference schema version is invalid after migration."));
        }

        if (!document.AlwaysOnTop.HasValue)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field alwaysOnTop is missing."));
        }

        if (string.IsNullOrWhiteSpace(document.SelectedView))
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field selectedView is missing."));
        }

        if (string.IsNullOrWhiteSpace(document.CompactAccountLayout))
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field compactAccountLayout is missing."));
        }

        if (!document.WidgetScalePercent.HasValue)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field widgetScalePercent is missing."));
        }

        if (!document.RefreshPeriodSeconds.HasValue)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field refreshPeriodSeconds is missing."));
        }

        if (string.IsNullOrWhiteSpace(document.Theme))
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field theme is missing."));
        }

        if (document.WorkSchedule is null)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field workSchedule is missing."));
        }

        if (document.QuotaThresholds is null)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field quotaThresholds is missing."));
        }

        if (document.WindowPlacement is null)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field windowPlacement is missing."));
        }

        if (diagnostics.Count > 0)
        {
            return new WidgetPreferenceValidationResult(null, diagnostics);
        }

        if (!Enum.TryParse<WidgetViewKind>(document.SelectedView, ignoreCase: true, out var selectedView)
            || !Enum.IsDefined(selectedView))
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.Malformed, $"Preference selectedView '{document.SelectedView}' is not supported."));
        }

        if (!Enum.TryParse<CompactAccountLayout>(document.CompactAccountLayout, ignoreCase: true, out var compactAccountLayout)
            || !Enum.IsDefined(compactAccountLayout))
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.Malformed, $"Preference compactAccountLayout '{document.CompactAccountLayout}' is not supported."));
        }

        if (!Enum.TryParse<WidgetThemePreference>(document.Theme, ignoreCase: true, out var theme)
            || !Enum.IsDefined(theme))
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.Malformed, $"Preference theme '{document.Theme}' is not supported."));
        }

        var normalizedRefreshPeriodSeconds = Math.Clamp(
            document.RefreshPeriodSeconds!.Value,
            WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds,
            WidgetPreferenceDefaults.MaximumRefreshPeriodSeconds);
        var normalizedWidgetScalePercent = NormalizeWidgetScalePercent(document.WidgetScalePercent!.Value);
        var workSchedule = BuildWorkSchedule(document.WorkSchedule!, diagnostics);
        var quotaThresholds = BuildQuotaThresholds(document.QuotaThresholds!, diagnostics);

        var placement = document.WindowPlacement!;
        if (!placement.X.HasValue || !placement.Y.HasValue || !placement.Width.HasValue || !placement.Height.HasValue)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "One or more windowPlacement fields are missing."));
        }

        if (diagnostics.Count > 0)
        {
            return new WidgetPreferenceValidationResult(null, diagnostics);
        }

        if (placement.Width!.Value <= 0 || placement.Height!.Value <= 0)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.Malformed, "windowPlacement width and height must be greater than zero."));
        }

        if (diagnostics.Count > 0)
        {
            return new WidgetPreferenceValidationResult(null, diagnostics);
        }

        var normalized = new WidgetPreferences
        {
            SchemaVersion = document.SchemaVersion!.Value,
            SelectedView = selectedView,
            CompactAccountLayout = compactAccountLayout,
            WidgetScalePercent = normalizedWidgetScalePercent,
            AlwaysOnTop = document.AlwaysOnTop!.Value,
            RefreshPeriodSeconds = normalizedRefreshPeriodSeconds,
            Theme = theme,
            WorkSchedule = workSchedule!,
            QuotaThresholds = quotaThresholds!,
            WindowPlacement = new WindowPlacementPreferences
            {
                X = placement.X!.Value,
                Y = placement.Y!.Value,
                Width = placement.Width!.Value,
                Height = placement.Height!.Value,
                ScreenKey = string.IsNullOrWhiteSpace(placement.ScreenKey) ? null : placement.ScreenKey,
            },
        };

        return new WidgetPreferenceValidationResult(normalized, diagnostics);
    }

    private static int NormalizeWidgetScalePercent(int percent)
    {
        var clamped = Math.Clamp(
            percent,
            WidgetPreferenceDefaults.MinimumWidgetScalePercent,
            WidgetPreferenceDefaults.MaximumWidgetScalePercent);
        var step = WidgetPreferenceDefaults.WidgetScaleStepPercent;
        var offset = clamped - WidgetPreferenceDefaults.MinimumWidgetScalePercent;
        var roundedOffset = (int)Math.Round(offset / (double)step, MidpointRounding.AwayFromZero) * step;
        return Math.Clamp(
            WidgetPreferenceDefaults.MinimumWidgetScalePercent + roundedOffset,
            WidgetPreferenceDefaults.MinimumWidgetScalePercent,
            WidgetPreferenceDefaults.MaximumWidgetScalePercent);
    }

    private static SourceDiagnostic CreateDiagnostic(SourceDiagnosticCode code, string summary)
    {
        return new SourceDiagnostic
        {
            Code = code,
            Severity = SourceDiagnosticSeverity.Error,
            Summary = summary,
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static WeeklyWorkSchedule? BuildWorkSchedule(
        WeeklyWorkScheduleDocument document,
        List<SourceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var monday = BuildDaySchedule("monday", document.Monday, diagnostics);
        var tuesday = BuildDaySchedule("tuesday", document.Tuesday, diagnostics);
        var wednesday = BuildDaySchedule("wednesday", document.Wednesday, diagnostics);
        var thursday = BuildDaySchedule("thursday", document.Thursday, diagnostics);
        var friday = BuildDaySchedule("friday", document.Friday, diagnostics);
        var saturday = BuildDaySchedule("saturday", document.Saturday, diagnostics);
        var sunday = BuildDaySchedule("sunday", document.Sunday, diagnostics);

        if (diagnostics.Count > 0)
        {
            return null;
        }

        var schedule = new WeeklyWorkSchedule
        {
            Monday = monday!,
            Tuesday = tuesday!,
            Wednesday = wednesday!,
            Thursday = thursday!,
            Friday = friday!,
            Saturday = saturday!,
            Sunday = sunday!,
        };

        foreach (var issue in UsageConfigurationRules.ValidateWeeklyWorkSchedule(schedule))
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.Malformed,
                $"Preference workSchedule {issue.Path} is invalid: {issue.Message}"));
        }

        return diagnostics.Count > 0 ? null : schedule;
    }

    private static DayWorkSchedule? BuildDaySchedule(
        string dayName,
        WorkWindowDocument[]? documents,
        List<SourceDiagnostic> diagnostics)
    {
        if (documents is null)
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.MissingRequiredField,
                $"Preference field workSchedule.{dayName} is missing."));
            return null;
        }

        var windows = new List<WorkWindow>(documents.Length);
        for (var index = 0; index < documents.Length; index++)
        {
            var document = documents[index];
            if (document is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.Malformed,
                    $"Preference workSchedule.{dayName}[{index}] is missing."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(document.Start))
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.MissingRequiredField,
                    $"Preference field workSchedule.{dayName}[{index}].start is missing."));
            }

            if (string.IsNullOrWhiteSpace(document.End))
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.MissingRequiredField,
                    $"Preference field workSchedule.{dayName}[{index}].end is missing."));
            }

            if (string.IsNullOrWhiteSpace(document.Start) || string.IsNullOrWhiteSpace(document.End))
            {
                continue;
            }

            if (!TryParseTime(document.Start!, out var start))
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.Malformed,
                    $"Preference workSchedule.{dayName}[{index}].start '{document.Start}' is not a valid HH:mm time."));
            }

            if (!TryParseTime(document.End!, out var end))
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.Malformed,
                    $"Preference workSchedule.{dayName}[{index}].end '{document.End}' is not a valid HH:mm time."));
            }

            if (!TryParseTime(document.Start!, out start) || !TryParseTime(document.End!, out end))
            {
                continue;
            }

            windows.Add(new WorkWindow
            {
                Start = start,
                End = end,
            });
        }

        return new DayWorkSchedule
        {
            Windows = windows.ToArray(),
        };
    }

    private static QuotaThresholds? BuildQuotaThresholds(
        QuotaThresholdsDocument document,
        List<SourceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!document.RedBelowPercent.HasValue)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field quotaThresholds.redBelowPercent is missing."));
        }

        if (!document.YellowBelowPercent.HasValue)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field quotaThresholds.yellowBelowPercent is missing."));
        }

        if (!document.BlueAbovePercent.HasValue)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field quotaThresholds.blueAbovePercent is missing."));
        }

        if (!document.PinkAbovePercent.HasValue)
        {
            diagnostics.Add(CreateDiagnostic(SourceDiagnosticCode.MissingRequiredField, "Preference field quotaThresholds.pinkAbovePercent is missing."));
        }

        if (diagnostics.Count > 0)
        {
            return null;
        }

        var thresholds = new QuotaThresholds
        {
            RedBelowPercent = document.RedBelowPercent!.Value,
            YellowBelowPercent = document.YellowBelowPercent!.Value,
            BlueAbovePercent = document.BlueAbovePercent!.Value,
            PinkAbovePercent = document.PinkAbovePercent!.Value,
        };

        foreach (var issue in UsageConfigurationRules.ValidateQuotaThresholds(thresholds))
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.Malformed,
                $"Preference quotaThresholds.{issue.Path} is invalid: {issue.Message}"));
        }

        return diagnostics.Count > 0 ? null : thresholds;
    }

    private static bool TryParseTime(string text, out TimeOnly time)
    {
        return TimeOnly.TryParseExact(
            text,
            ["H:mm", "HH:mm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);
    }
}
