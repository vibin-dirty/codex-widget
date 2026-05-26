using CodexWidget.Core;

namespace CodexWidget.App;

internal sealed record WidgetPreferenceDraft
{
    public WidgetViewKind SelectedView { get; init; } = WidgetPreferenceDefaults.DefaultSelectedView;

    public CompactAccountLayout CompactAccountLayout { get; init; } = WidgetPreferenceDefaults.DefaultCompactAccountLayout;

    public int WidgetScalePercent { get; init; } = WidgetPreferenceDefaults.DefaultWidgetScalePercent;

    public bool AlwaysOnTop { get; init; } = WidgetPreferenceDefaults.DefaultAlwaysOnTop;

    public int RefreshPeriodSeconds { get; init; } = WidgetPreferenceDefaults.DefaultRefreshPeriodSeconds;

    public WidgetThemePreference Theme { get; init; } = WidgetPreferenceDefaults.DefaultTheme;

    public WeeklyWorkSchedule WorkSchedule { get; init; } = UsageConfigurationDefaults.CreateDefaultWeeklyWorkSchedule();

    public QuotaThresholds QuotaThresholds { get; init; } = UsageConfigurationDefaults.CreateDefaultQuotaThresholds();
}

internal sealed record WidgetPreferenceSaveOutcome
{
    public bool Succeeded { get; init; }

    public WidgetPreferences? AppliedPreferences { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}

internal sealed class WidgetPreferenceCoordinator
{
    private readonly PreferenceStore _preferenceStore;
    private readonly Action<WidgetPreferences> _applyPreferences;
    private WidgetPreferences _currentPreferences;

    public WidgetPreferenceCoordinator(
        PreferenceStore preferenceStore,
        WidgetPreferences currentPreferences,
        Action<WidgetPreferences> applyPreferences)
    {
        _preferenceStore = preferenceStore ?? throw new ArgumentNullException(nameof(preferenceStore));
        _currentPreferences = currentPreferences ?? throw new ArgumentNullException(nameof(currentPreferences));
        _applyPreferences = applyPreferences ?? throw new ArgumentNullException(nameof(applyPreferences));
    }

    public WidgetPreferenceDraft CreateDraft()
    {
        return new WidgetPreferenceDraft
        {
            SelectedView = _currentPreferences.SelectedView,
            CompactAccountLayout = _currentPreferences.CompactAccountLayout,
            WidgetScalePercent = _currentPreferences.WidgetScalePercent,
            AlwaysOnTop = _currentPreferences.AlwaysOnTop,
            RefreshPeriodSeconds = _currentPreferences.RefreshPeriodSeconds,
            Theme = _currentPreferences.Theme,
            WorkSchedule = _currentPreferences.WorkSchedule,
            QuotaThresholds = _currentPreferences.QuotaThresholds,
        };
    }

    public WidgetPreferenceDraft NormalizeDraft(WidgetPreferenceDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return draft with
        {
            SelectedView = NormalizeVisibleSelectedView(draft.SelectedView),
            CompactAccountLayout = NormalizeCompactAccountLayout(draft.CompactAccountLayout),
            WidgetScalePercent = NormalizeWidgetScalePercent(draft.WidgetScalePercent),
            RefreshPeriodSeconds = Math.Clamp(
                draft.RefreshPeriodSeconds,
                WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds,
                WidgetPreferenceDefaults.MaximumRefreshPeriodSeconds),
            Theme = NormalizeTheme(draft.Theme),
            WorkSchedule = draft.WorkSchedule,
            QuotaThresholds = draft.QuotaThresholds,
        };
    }

    public WidgetPreferenceSaveOutcome SaveAndApply(WidgetPreferenceDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var normalized = NormalizeDraft(draft);
        var messages = BuildNormalizationMessages(draft, normalized);
        var preferences = BuildPreferences(normalized);
        var saveResult = _preferenceStore.Save(preferences);

        if (!saveResult.Availability.IsAvailable)
        {
            return new WidgetPreferenceSaveOutcome
            {
                Succeeded = false,
                AppliedPreferences = null,
                Messages = BuildFailureMessages(saveResult.Diagnostics),
            };
        }

        var appliedPreferences = ResolveSavedPreferences(preferences);
        _currentPreferences = appliedPreferences;

        try
        {
            _applyPreferences(appliedPreferences);
        }
        catch (Exception exception)
        {
            var saveMessages = new List<string>(messages)
            {
                $"Preferences were saved, but applying them in the running widget failed: {RedactionHelper.RedactDiagnosticValue("error", exception.Message)}",
            };

            return new WidgetPreferenceSaveOutcome
            {
                Succeeded = false,
                AppliedPreferences = appliedPreferences,
                Messages = saveMessages,
            };
        }

        if (messages.Count == 0)
        {
            messages.Add("Preferences saved.");
        }

        return new WidgetPreferenceSaveOutcome
        {
            Succeeded = true,
            AppliedPreferences = appliedPreferences,
            Messages = messages,
        };
    }

    public WidgetPreferenceSaveOutcome ToggleCompactLayoutAndApply(WidgetViewKind selectedVisibleView)
    {
        var draft = CreateDraft();
        return SaveAndApply(draft with
        {
            SelectedView = NormalizeVisibleSelectedView(selectedVisibleView),
            CompactAccountLayout = ToggleCompactAccountLayout(draft.CompactAccountLayout),
        });
    }

    public WidgetPreferenceSaveOutcome AdjustWidgetScaleAndApply(WidgetViewKind selectedVisibleView, int deltaPercent)
    {
        var draft = CreateDraft();
        return SaveAndApply(draft with
        {
            SelectedView = NormalizeVisibleSelectedView(selectedVisibleView),
            WidgetScalePercent = draft.WidgetScalePercent + deltaPercent,
        });
    }

    public void UpdateCurrentPreferences(WidgetPreferences preferences)
    {
        _currentPreferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    private WidgetPreferences BuildPreferences(WidgetPreferenceDraft draft)
    {
        return _currentPreferences with
        {
            SelectedView = draft.SelectedView,
            CompactAccountLayout = draft.CompactAccountLayout,
            WidgetScalePercent = draft.WidgetScalePercent,
            AlwaysOnTop = draft.AlwaysOnTop,
            RefreshPeriodSeconds = draft.RefreshPeriodSeconds,
            Theme = draft.Theme,
            WorkSchedule = draft.WorkSchedule,
            QuotaThresholds = draft.QuotaThresholds,
        };
    }

    private WidgetPreferences ResolveSavedPreferences(WidgetPreferences fallbackPreferences)
    {
        var loadResult = _preferenceStore.Load();
        if (loadResult.Availability.IsAvailable && !loadResult.UsedDefaults)
        {
            return loadResult.Preferences;
        }

        return fallbackPreferences;
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

    private static WidgetViewKind NormalizeVisibleSelectedView(WidgetViewKind selectedView)
    {
        return selectedView switch
        {
            WidgetViewKind.Minimal => WidgetViewKind.Minimal,
            WidgetViewKind.Compact => WidgetViewKind.Compact,
            WidgetViewKind.Full => WidgetViewKind.Compact,
            _ => WidgetViewKind.Compact,
        };
    }

    private static CompactAccountLayout NormalizeCompactAccountLayout(CompactAccountLayout compactAccountLayout)
    {
        return Enum.IsDefined(compactAccountLayout)
            ? compactAccountLayout
            : WidgetPreferenceDefaults.DefaultCompactAccountLayout;
    }

    private static CompactAccountLayout ToggleCompactAccountLayout(CompactAccountLayout compactAccountLayout)
    {
        return NormalizeCompactAccountLayout(compactAccountLayout) == CompactAccountLayout.Horizontal
            ? CompactAccountLayout.Vertical
            : CompactAccountLayout.Horizontal;
    }

    private static WidgetThemePreference NormalizeTheme(WidgetThemePreference theme)
    {
        return Enum.IsDefined(theme)
            ? theme
            : WidgetPreferenceDefaults.DefaultTheme;
    }

    private static List<string> BuildNormalizationMessages(WidgetPreferenceDraft draft, WidgetPreferenceDraft normalized)
    {
        var messages = new List<string>();

        if (draft.SelectedView != normalized.SelectedView)
        {
            messages.Add("Selected view was normalized to Compact for visible runtime modes.");
        }

        if (draft.CompactAccountLayout != normalized.CompactAccountLayout)
        {
            messages.Add($"Compact account layout was normalized to {normalized.CompactAccountLayout}.");
        }

        if (draft.RefreshPeriodSeconds != normalized.RefreshPeriodSeconds)
        {
            messages.Add(
                $"Refresh period was normalized to {normalized.RefreshPeriodSeconds} seconds ({WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds}-{WidgetPreferenceDefaults.MaximumRefreshPeriodSeconds}).");
        }

        if (draft.WidgetScalePercent != normalized.WidgetScalePercent)
        {
            messages.Add(
                $"Widget scale was normalized to {normalized.WidgetScalePercent}% ({WidgetPreferenceDefaults.MinimumWidgetScalePercent}-{WidgetPreferenceDefaults.MaximumWidgetScalePercent}).");
        }

        if (draft.Theme != normalized.Theme)
        {
            messages.Add($"Theme was normalized to {normalized.Theme}.");
        }

        return messages;
    }

    private static IReadOnlyList<string> BuildFailureMessages(IReadOnlyList<SourceDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return ["Preferences could not be saved."];
        }

        return diagnostics
            .Select(diagnostic =>
            {
                var summary = RedactionHelper.RedactDiagnosticValue("summary", diagnostic.Summary);
                var detail = RedactionHelper.RedactDiagnosticValue("detail", diagnostic.Detail);
                return string.Equals(detail, RedactionHelper.RedactedMarker, StringComparison.Ordinal)
                    ? summary
                    : $"{summary} ({detail})";
            })
            .ToArray();
    }
}
