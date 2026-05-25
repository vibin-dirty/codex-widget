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
            1 => new WidgetPreferenceMigrationResult(MigrateSchema4ToSchema5(MigrateSchema3ToSchema4(MigrateSchema2ToSchema3(MigrateSchema1ToSchema2(document)))), null),
            2 => new WidgetPreferenceMigrationResult(MigrateSchema4ToSchema5(MigrateSchema3ToSchema4(MigrateSchema2ToSchema3(document))), null),
            3 => new WidgetPreferenceMigrationResult(MigrateSchema4ToSchema5(MigrateSchema3ToSchema4(document)), null),
            4 => new WidgetPreferenceMigrationResult(MigrateSchema4ToSchema5(document), null),
            5 => new WidgetPreferenceMigrationResult(document, null),
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
            SchemaVersion = WidgetPreferenceDefaults.CurrentSchemaVersion,
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
}
