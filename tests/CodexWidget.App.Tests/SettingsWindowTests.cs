using System.Collections;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using CodexWidget.Core;

namespace CodexWidget.App.Tests;

public sealed class SettingsWindowTests
{
    [Fact]
    public void ConfigureThemeSelector_ExposesLightAndDarkChoices()
    {
        var selector = new ComboBox();

        SettingsWindow.ConfigureThemeSelector(selector);

        Assert.Equal(SettingsWindow.ThemeSelectorAutomationName, AutomationProperties.GetName(selector));
        Assert.Equal(HorizontalAlignment.Stretch, selector.HorizontalAlignment);
        Assert.Equal(180, selector.MinWidth);
        Assert.Equal(
            [WidgetThemePreference.Light, WidgetThemePreference.Dark],
            Assert.IsAssignableFrom<IEnumerable>(selector.ItemsSource).Cast<WidgetThemePreference>().ToArray());
    }

    [Fact]
    public void Editor_DefaultDraftShowsConfiguredWeeklyHoursAndCanSave()
    {
        var schedule = WidgetPreferenceDefaults.Create().WorkSchedule;
        var thresholds = WidgetPreferenceDefaults.Create().QuotaThresholds;

        var validation = SettingsWindow.ValidateEditorForTests(schedule, thresholds, []);

        Assert.True(validation.IsValid);
        Assert.Contains("56.00 h", SettingsWindow.FormatWeeklyHoursForTests(schedule), StringComparison.Ordinal);
        Assert.Equal(70, thresholds.RedBelowPercent);
        Assert.Equal(130, thresholds.PinkAbovePercent);
    }

    [Fact]
    public void Editor_CopySourceDayToSelectedDaysSavesThroughCoordinatorDraft()
    {
        using var directory = new TemporaryDirectory();
        var applied = new List<WidgetPreferences>();
        var currentPreferences = WidgetPreferenceDefaults.Create() with
        {
            WorkSchedule = new WeeklyWorkSchedule
            {
                Monday = new DayWorkSchedule
                {
                    Windows =
                    [
                        new WorkWindow { Start = new TimeOnly(8, 0), End = new TimeOnly(12, 0) },
                        new WorkWindow { Start = new TimeOnly(13, 0), End = new TimeOnly(15, 0) },
                    ],
                },
            },
            QuotaThresholds = new QuotaThresholds
            {
                RedBelowPercent = 65,
                YellowBelowPercent = 85,
                BlueAbovePercent = 115,
                PinkAbovePercent = 140,
            },
        };
        var coordinator = CreateCoordinator(directory.Path, currentPreferences, applied.Add);

        var schedule = SettingsWindow.CopyDayToSelectedDaysForTests(
            currentPreferences.WorkSchedule,
            DayOfWeek.Monday,
            [DayOfWeek.Tuesday, DayOfWeek.Wednesday]);
        var outcome = coordinator.SaveAndApply(coordinator.CreateDraft() with
        {
            WorkSchedule = schedule,
            QuotaThresholds = currentPreferences.QuotaThresholds,
        });

        Assert.True(outcome.Succeeded);
        var preferences = Assert.Single(applied);
        Assert.Equal(new TimeOnly(8, 0), preferences.WorkSchedule.Tuesday.Windows[0].Start);
        Assert.Equal(new TimeOnly(15, 0), preferences.WorkSchedule.Wednesday.Windows[1].End);
        Assert.Equal(140, preferences.QuotaThresholds.PinkAbovePercent);
    }

    [Fact]
    public void Editor_ClearAllDaysAllowsZeroWeeklyHours()
    {
        var schedule = SettingsWindow.ClearDaysForTests(
            WidgetPreferenceDefaults.Create().WorkSchedule,
            [
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday,
                DayOfWeek.Sunday,
            ]);
        var validation = SettingsWindow.ValidateEditorForTests(
            schedule,
            UsageConfigurationDefaults.CreateDefaultQuotaThresholds(),
            []);

        Assert.True(validation.IsValid);
        Assert.Empty(schedule.Monday.Windows);
        Assert.Empty(schedule.Sunday.Windows);
        Assert.Contains("0.00 h", SettingsWindow.FormatWeeklyHoursForTests(schedule), StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_SelectingAdditionalDayDoesNotMutateThatDaySchedule()
    {
        var schedule = CreateDistinctWeekdayAndSaturdaySchedule();

        var preview = SettingsWindow.PreviewSelectionForTests(
            schedule,
            [
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday,
            ]);

        Assert.Equal(schedule.Saturday, preview.WorkSchedule.Saturday);
        Assert.Equal(new TimeOnly(10, 0), preview.WorkSchedule.Saturday.Windows[0].Start);
        Assert.Equal(new TimeOnly(12, 0), preview.WorkSchedule.Saturday.Windows[0].End);
    }

    [Fact]
    public void Editor_SelectingDaysWithIdenticalSchedulesShowsCommonRowsAndAllowsDirectActions()
    {
        var schedule = CreateDistinctWeekdayAndSaturdaySchedule();

        var preview = SettingsWindow.PreviewSelectionForTests(
            schedule,
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Friday]);

        Assert.Equal("Editing: Mon, Tue, Fri", preview.EditingDaysText);
        Assert.Equal([("08:00", "16:00")], preview.SelectedRows);
        Assert.False(preview.HasMixedSchedules);
        Assert.True(preview.CanEditWindows);
        Assert.Equal(string.Empty, preview.ScheduleDifferenceText);
        Assert.Equal(schedule, preview.WorkSchedule);
    }

    [Fact]
    public void Editor_SelectingDaysWithDifferentSchedulesShowsGroupedDifferenceAndDisablesDirectActions()
    {
        var schedule = CreateDistinctWeekdaySaturdayAndEmptySundaySchedule();

        var preview = SettingsWindow.PreviewSelectionForTests(
            schedule,
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Saturday, DayOfWeek.Sunday]);

        Assert.Equal("Editing: Mon, Tue, Sat, Sun", preview.EditingDaysText);
        Assert.Empty(preview.SelectedRows);
        Assert.True(preview.HasMixedSchedules);
        Assert.False(preview.CanEditWindows);
        Assert.Contains("Schedules differ among selected days.", preview.ScheduleDifferenceText, StringComparison.Ordinal);
        Assert.Contains("Mon, Tue: 08:00-16:00", preview.ScheduleDifferenceText, StringComparison.Ordinal);
        Assert.Contains("Sat: 10:00-12:00", preview.ScheduleDifferenceText, StringComparison.Ordinal);
        Assert.Contains("Sun: no work windows", preview.ScheduleDifferenceText, StringComparison.Ordinal);
        Assert.Contains("Use Copy Monday to selected days", preview.ScheduleDifferenceText, StringComparison.Ordinal);
        Assert.Equal(schedule, preview.WorkSchedule);
    }

    [Fact]
    public void Editor_CopyingSourceDayToMixedSelectionEqualizesSchedulesAndRestoresEditing()
    {
        var selectedDays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var schedule = CreateDistinctWeekdaySaturdayAndEmptySundaySchedule();

        var equalized = SettingsWindow.CopyDayToSelectedDaysForTests(
            schedule,
            DayOfWeek.Saturday,
            selectedDays);
        var preview = SettingsWindow.PreviewSelectionForTests(equalized, selectedDays);

        Assert.Equal(equalized.Saturday, equalized.Monday);
        Assert.Equal(equalized.Saturday, equalized.Tuesday);
        Assert.Equal(equalized.Saturday, equalized.Sunday);
        Assert.Equal([("10:00", "12:00")], preview.SelectedRows);
        Assert.False(preview.HasMixedSchedules);
        Assert.True(preview.CanEditWindows);
        Assert.Equal(string.Empty, preview.ScheduleDifferenceText);
    }

    [Fact]
    public void Editor_SelectingSaturdayAloneShowsSaturdaySchedule()
    {
        var schedule = CreateDistinctWeekdayAndSaturdaySchedule();

        var preview = SettingsWindow.PreviewSelectionForTests(schedule, [DayOfWeek.Saturday]);

        Assert.Equal("Editing: Sat", preview.EditingDaysText);
        Assert.Equal([("10:00", "12:00")], preview.SelectedRows);
        Assert.True(preview.CanEditWindows);
        Assert.False(preview.HasMixedSchedules);
        Assert.Equal(schedule, preview.WorkSchedule);
    }

    [Fact]
    public void Editor_DeselectingAllDaysIsNeutralAndDoesNotMutateSchedule()
    {
        var schedule = CreateDistinctWeekdayAndSaturdaySchedule();

        var preview = SettingsWindow.PreviewSelectionForTests(schedule, []);
        var afterNoSelectionEdit = SettingsWindow.ApplyRowsToSelectedDaysForTests(
            schedule,
            [],
            [("09:00", "17:00")]);

        Assert.Equal("Editing: no days selected", preview.EditingDaysText);
        Assert.Empty(preview.SelectedRows);
        Assert.False(preview.CanEditWindows);
        Assert.False(preview.HasMixedSchedules);
        Assert.Equal(schedule, preview.WorkSchedule);
        Assert.Equal(schedule, afterNoSelectionEdit);
    }

    [Fact]
    public void Editor_InvalidScheduleAndThresholdsDisableSave()
    {
        using var directory = new TemporaryDirectory();
        var applied = new List<WidgetPreferences>();
        var coordinator = CreateCoordinator(directory.Path, WidgetPreferenceDefaults.Create(), applied.Add);
        var schedule = SettingsWindow.ApplyRowsToDaysForTests(
            WidgetPreferenceDefaults.Create().WorkSchedule,
            [DayOfWeek.Monday],
            [("08:00", "12:00"), ("11:30", "15:30"), ("17:00", "16:00")]);
        var thresholds = new QuotaThresholds
        {
            RedBelowPercent = 70,
            YellowBelowPercent = 65,
            BlueAbovePercent = 110,
            PinkAbovePercent = 130,
        };

        var validation = SettingsWindow.ValidateEditorForTests(
            schedule,
            thresholds,
            [("08:00", "12:00"), ("11:30", "15:30"), ("17:00", "16:00")]);
        var outcome = coordinator.SaveAndApply(coordinator.CreateDraft() with
        {
            WorkSchedule = schedule,
            QuotaThresholds = thresholds,
        });

        Assert.False(validation.IsValid);
        Assert.False(outcome.Succeeded);
        Assert.Contains(validation.ThresholdIssues, issue => issue.Message.Contains("Yellow threshold", StringComparison.Ordinal));
        Assert.Empty(applied);
    }

    [Fact]
    public void Editor_RowValidationWithoutErrorCollapsesEmptyMessage()
    {
        var presentation = SettingsWindow.ApplyRowValidationMessageForTests(null);

        Assert.Equal(string.Empty, presentation.ErrorText);
        Assert.False(presentation.IsErrorVisible);
        Assert.False(presentation.UsesErrorBorder);
    }

    [Fact]
    public void Editor_RowValidationWithErrorShowsMessageAndErrorBorder()
    {
        var presentation = SettingsWindow.ApplyRowValidationMessageForTests("End time must be after start time.");

        Assert.Equal("End time must be after start time.", presentation.ErrorText);
        Assert.True(presentation.IsErrorVisible);
        Assert.True(presentation.UsesErrorBorder);
    }

    private static WeeklyWorkSchedule CreateDistinctWeekdayAndSaturdaySchedule()
    {
        var schedule = SettingsWindow.ApplyRowsToDaysForTests(
            WidgetPreferenceDefaults.Create().WorkSchedule,
            [
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
            ],
            [("08:00", "16:00")]);

        return SettingsWindow.ApplyRowsToDaysForTests(
            schedule,
            [DayOfWeek.Saturday],
            [("10:00", "12:00")]);
    }

    private static WeeklyWorkSchedule CreateDistinctWeekdaySaturdayAndEmptySundaySchedule()
    {
        return SettingsWindow.ClearDaysForTests(
            CreateDistinctWeekdayAndSaturdaySchedule(),
            [DayOfWeek.Sunday]);
    }

    private static WidgetPreferenceCoordinator CreateCoordinator(
        string directoryPath,
        WidgetPreferences currentPreferences,
        Action<WidgetPreferences> applyPreferences)
    {
        var filePath = Path.Combine(directoryPath, "settings.json");
        return new WidgetPreferenceCoordinator(
            new PreferenceStore(new FixedPreferencePathProvider(filePath)),
            currentPreferences,
            applyPreferences);
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
}
