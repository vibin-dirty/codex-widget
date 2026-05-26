using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CodexWidget.Core;

namespace CodexWidget.App;

internal sealed class SettingsWindow : Window
{
    internal const string ThemeSelectorAutomationName = "Theme";
    private const double FieldRowWidth = 500;
    private const double ScrollbarContentRightInset = 18;

    private static readonly DayOfWeek[] OrderedDays =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    private static readonly DayOfWeek[] Weekdays =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
    ];

    private static readonly DayOfWeek[] Weekend =
    [
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    private readonly WidgetPreferenceCoordinator _preferenceCoordinator;
    private readonly ComboBox _selectedViewComboBox;
    private readonly ComboBox _themeComboBox;
    private readonly NumericUpDown _widgetScaleInput;
    private readonly CheckBox _alwaysOnTopCheckBox;
    private readonly NumericUpDown _refreshPeriodInput;
    private readonly TextBlock _statusTextBlock;
    private readonly Border _contentBorder;
    private readonly StackPanel _settingsStack;
    private readonly StackPanel _dayTogglePanel;
    private readonly TextBlock _editingDaysTextBlock;
    private readonly StackPanel _workWindowRowsPanel;
    private readonly TextBlock _weeklyHoursTextBlock;
    private readonly TextBlock _zeroWeeklyHoursTextBlock;
    private readonly TextBlock _scheduleActionTextBlock;
    private readonly ComboBox _copySourceComboBox;
    private readonly Button _addWindowButton;
    private readonly Button _removeLastWindowButton;
    private readonly NumericUpDown _redThresholdInput;
    private readonly NumericUpDown _yellowThresholdInput;
    private readonly NumericUpDown _blueThresholdInput;
    private readonly NumericUpDown _pinkThresholdInput;
    private readonly TextBlock _thresholdValidationTextBlock;
    private readonly Button _saveButton;
    private readonly List<TextBlock> _fieldLabels = [];
    private readonly List<TextBlock> _hintTextBlocks = [];
    private readonly List<Border> _sectionBorders = [];
    private readonly Dictionary<DayOfWeek, ToggleButton> _dayToggleButtons = [];
    private readonly Dictionary<DayOfWeek, List<WorkWindowEditorRow>> _workScheduleRows = [];
    private readonly HashSet<DayOfWeek> _selectedDays = [];
    private bool _isRefreshingControls;

    public SettingsWindow(WindowIcon? icon, WidgetPreferenceCoordinator preferenceCoordinator)
    {
        _preferenceCoordinator = preferenceCoordinator ?? throw new ArgumentNullException(nameof(preferenceCoordinator));

        Title = "Settings";
        Icon = icon;
        Width = 860;
        Height = 900;
        MinWidth = 820;
        MinHeight = 640;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _selectedViewComboBox = new ComboBox
        {
            ItemsSource = new[] { WidgetViewKind.Minimal, WidgetViewKind.Compact, WidgetViewKind.Full },
            SelectedIndex = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Width = 200,
        };
        _themeComboBox = new ComboBox
        {
            SelectedIndex = 0,
            Width = 200,
        };
        ConfigureThemeSelector(_themeComboBox);
        _themeComboBox.SelectionChanged += (_, _) =>
        {
            if (!_isRefreshingControls)
            {
                ApplyThemeStyles(ResolveSelectedTheme());
            }
        };

        _alwaysOnTopCheckBox = new CheckBox
        {
            Content = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _widgetScaleInput = CreateNumericInput(
            WidgetPreferenceDefaults.MinimumWidgetScalePercent,
            WidgetPreferenceDefaults.MaximumWidgetScalePercent,
            WidgetPreferenceDefaults.WidgetScaleStepPercent);
        _refreshPeriodInput = CreateNumericInput(
            WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds,
            WidgetPreferenceDefaults.MaximumRefreshPeriodSeconds,
            30);
        _statusTextBlock = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 580,
        };

        _settingsStack = new StackPanel
        {
            Spacing = 10,
        };
        _dayTogglePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        _editingDaysTextBlock = CreateHintText(string.Empty);
        _workWindowRowsPanel = new StackPanel
        {
            Spacing = 0,
        };
        _weeklyHoursTextBlock = CreateHintText(string.Empty);
        _zeroWeeklyHoursTextBlock = CreateHintText("Weekly time-left percent will be unavailable when total weekly hours are 0.");
        _scheduleActionTextBlock = CreateHintText(string.Empty);
        _copySourceComboBox = new ComboBox
        {
            ItemsSource = OrderedDays.Select(day => new DayChoice(day)).ToArray(),
            SelectedIndex = 0,
            MinWidth = 190,
        };
        _addWindowButton = new Button
        {
            Content = "Add window",
            MinWidth = 105,
        };
        _removeLastWindowButton = new Button
        {
            Content = "Remove last",
            MinWidth = 105,
        };
        _redThresholdInput = CreateThresholdInput();
        _yellowThresholdInput = CreateThresholdInput();
        _blueThresholdInput = CreateThresholdInput();
        _pinkThresholdInput = CreateThresholdInput();
        _thresholdValidationTextBlock = CreateHintText("Values must be in ascending order: Red < Yellow < Blue < Pink.");

        _saveButton = new Button
        {
            Content = "Save",
            MinWidth = 90,
        };
        _saveButton.Click += OnSaveClicked;

        _contentBorder = new Border
        {
            Padding = new Thickness(16),
            Child = BuildContent()
        };
        Content = _contentBorder;

        ReloadFromPreferences();
    }

    public void ReloadFromPreferences()
    {
        ApplyDraftToControls(_preferenceCoordinator.CreateDraft());
        _statusTextBlock.Text = string.Empty;
        ApplyThemeStyles(ResolveSelectedTheme());
    }

    internal SettingsWindowEditorSnapshot CreateEditorSnapshotForTests()
    {
        RefreshValidationState();
        var selectionState = BuildSelectionScheduleState(_workScheduleRows, _selectedDays);
        IEnumerable<WorkWindowEditorRow> visibleRows = selectionState.HasMixedSchedules
            ? []
            : GetSelectedRows();
        return new SettingsWindowEditorSnapshot(
            BuildWorkScheduleFromEditor(),
            BuildQuotaThresholdsFromControls(),
            _saveButton.IsEnabled,
            _weeklyHoursTextBlock.Text ?? string.Empty,
            _thresholdValidationTextBlock.Text ?? string.Empty,
            visibleRows
                .Select(row => (row.StartText, row.EndText))
                .ToArray(),
            _editingDaysTextBlock.Text ?? string.Empty,
            _scheduleActionTextBlock.Text ?? string.Empty,
            _addWindowButton.IsEnabled,
            _removeLastWindowButton.IsEnabled,
            selectionState.DifferenceText);
    }

    internal void SaveForTests()
    {
        OnSaveClicked(_saveButton, new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    internal void SelectDaysForTests(params DayOfWeek[] days)
    {
        SetSelectedDays(days);
    }

    internal void SetSelectedDayWindowsForTests(params (string Start, string End)[] windows)
    {
        SetRowsForSelectedDays(windows.Select(window => new WorkWindowEditorRow(window.Start, window.End)));
    }

    internal void ClearSelectedDaysForTests()
    {
        ClearSelectedDays();
    }

    internal void CopyDayToSelectedDaysForTests(DayOfWeek sourceDay)
    {
        CopyDayToSelectedDays(sourceDay);
    }

    internal void SetThresholdsForTests(int red, int yellow, int blue, int pink)
    {
        _redThresholdInput.Value = red;
        _yellowThresholdInput.Value = yellow;
        _blueThresholdInput.Value = blue;
        _pinkThresholdInput.Value = pink;
        RefreshValidationState();
    }

    internal static WeeklyWorkSchedule ApplyRowsToDaysForTests(
        WeeklyWorkSchedule schedule,
        IReadOnlyCollection<DayOfWeek> targetDays,
        IReadOnlyCollection<(string Start, string End)> rows)
    {
        var windowRows = rows
            .Select(row => new WorkWindowEditorRow(row.Start, row.End))
            .ToArray();

        return BuildScheduleFromDayResolver(day => targetDays.Contains(day)
            ? BuildDaySchedule(windowRows)
            : schedule.GetDaySchedule(day));
    }

    internal static WeeklyWorkSchedule CopyDayToSelectedDaysForTests(
        WeeklyWorkSchedule schedule,
        DayOfWeek sourceDay,
        IReadOnlyCollection<DayOfWeek> targetDays)
    {
        var sourceSchedule = schedule.GetDaySchedule(sourceDay);
        return BuildScheduleFromDayResolver(day => targetDays.Contains(day) ? sourceSchedule : schedule.GetDaySchedule(day));
    }

    internal static WeeklyWorkSchedule ClearDaysForTests(
        WeeklyWorkSchedule schedule,
        IReadOnlyCollection<DayOfWeek> targetDays)
    {
        return BuildScheduleFromDayResolver(day => targetDays.Contains(day)
            ? DayWorkSchedule.Empty
            : schedule.GetDaySchedule(day));
    }

    internal static SettingsWindowSelectionPreview PreviewSelectionForTests(
        WeeklyWorkSchedule schedule,
        IReadOnlyCollection<DayOfWeek> selectedDays)
    {
        var rowsByDay = CreateScheduleRows(schedule);
        var normalizedSelectedDays = NormalizeSelectedDays(selectedDays);
        var selectionState = BuildSelectionScheduleState(rowsByDay, normalizedSelectedDays);
        IEnumerable<WorkWindowEditorRow> visibleRows = selectionState.HasMixedSchedules
            ? []
            : GetSelectedRows(rowsByDay, normalizedSelectedDays);
        return new SettingsWindowSelectionPreview(
            BuildWorkSchedule(rowsByDay),
            visibleRows
                .Select(row => (row.StartText, row.EndText))
                .ToArray(),
            FormatEditingDaysText(normalizedSelectedDays),
            selectionState.CanEditWindows,
            selectionState.HasMixedSchedules,
            selectionState.DifferenceText);
    }

    internal static WeeklyWorkSchedule ApplyRowsToSelectedDaysForTests(
        WeeklyWorkSchedule schedule,
        IReadOnlyCollection<DayOfWeek> selectedDays,
        IReadOnlyCollection<(string Start, string End)> rows)
    {
        var rowsByDay = CreateScheduleRows(schedule);
        var normalizedSelectedDays = NormalizeSelectedDays(selectedDays);
        if (normalizedSelectedDays.Count == 0)
        {
            return BuildWorkSchedule(rowsByDay);
        }

        var rowSnapshot = rows
            .Select(row => new WorkWindowEditorRow(row.Start, row.End))
            .ToArray();
        foreach (var day in normalizedSelectedDays)
        {
            rowsByDay[day] = CloneRows(rowSnapshot);
        }

        return BuildWorkSchedule(rowsByDay);
    }

    internal static SettingsWindowEditorValidation ValidateEditorForTests(
        WeeklyWorkSchedule schedule,
        QuotaThresholds thresholds,
        IReadOnlyCollection<(string Start, string End)> visibleRows)
    {
        var invalidTimeRows = new Dictionary<int, string>();
        var rows = visibleRows.ToArray();
        for (var index = 0; index < rows.Length; index++)
        {
            if (!TryParseTime(rows[index].Start, out _) || !TryParseTime(rows[index].End, out _))
            {
                invalidTimeRows[index] = "Use HH:mm times such as 07:00 or 17:30.";
            }
        }

        return new SettingsWindowEditorValidation(
            schedule,
            UsageConfigurationRules.ValidateWeeklyWorkSchedule(schedule),
            UsageConfigurationRules.ValidateQuotaThresholds(thresholds),
            invalidTimeRows);
    }

    internal static string FormatWeeklyHoursForTests(WeeklyWorkSchedule schedule)
    {
        return FormatWeeklyHours(schedule);
    }

    internal static SettingsWindowRowValidationPresentation ApplyRowValidationMessageForTests(string? message)
    {
        var rowBorder = new Border();
        var error = new TextBlock();
        var palette = WidgetVisualStyles.ResolvePalette(WidgetThemePreference.Light);
        ApplyRowValidationMessage(rowBorder, error, message, palette);
        return new SettingsWindowRowValidationPresentation(
            error.Text ?? string.Empty,
            error.IsVisible,
            rowBorder.BorderBrush == palette.ErrorTextBrush);
    }

    private Control BuildContent()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 12,
        };

        _settingsStack.Children.Add(CreateFieldRow("Selected view", _selectedViewComboBox));
        _settingsStack.Children.Add(CreateFieldRow("Theme", _themeComboBox));
        _settingsStack.Children.Add(CreateFieldRow("Always on top", _alwaysOnTopCheckBox));
        _settingsStack.Children.Add(CreateFieldRow("Widget scale (%)", _widgetScaleInput));
        _settingsStack.Children.Add(CreateFieldRow("Refresh period (seconds)", _refreshPeriodInput));
        _settingsStack.Children.Add(CreateHintText($"Scale range: {WidgetPreferenceDefaults.MinimumWidgetScalePercent}% to {WidgetPreferenceDefaults.MaximumWidgetScalePercent}% in {WidgetPreferenceDefaults.WidgetScaleStepPercent}% steps."));
        _settingsStack.Children.Add(CreateHintText($"Allowed range: {WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds} to {WidgetPreferenceDefaults.MaximumRefreshPeriodSeconds} seconds."));
        _settingsStack.Children.Add(BuildScheduleSection());
        _settingsStack.Children.Add(BuildThresholdSection());
        _settingsStack.Children.Add(_statusTextBlock);

        var scrollerContent = new Border
        {
            Padding = new Thickness(0, 0, ScrollbarContentRightInset, 0),
            Child = _settingsStack,
        };

        var scroller = new ScrollViewer
        {
            Content = scrollerContent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroller, 0);
        root.Children.Add(scroller);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                new Button
                {
                    Content = "Cancel",
                    MinWidth = 90,
                },
                _saveButton,
            }
        };

        var cancelButton = (Button)buttons.Children[0]!;
        cancelButton.Click += (_, _) => Close();

        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        return root;
    }

    private Control BuildScheduleSection()
    {
        var panel = new StackPanel
        {
            Spacing = 9,
        };

        panel.Children.Add(CreateSectionTitle("Weekly work schedule"));
        panel.Children.Add(CreateShortcutRow());

        var editDaysRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                CreateInlineLabel("Edit days:"),
                _dayTogglePanel,
            }
        };

        foreach (var day in OrderedDays)
        {
            var button = new ToggleButton
            {
                Content = FormatShortDay(day),
                MinWidth = 52,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var capturedDay = day;
            button.IsCheckedChanged += (_, _) =>
            {
                if (_isRefreshingControls)
                {
                    return;
                }

                if (button.IsChecked == true)
                {
                    _selectedDays.Add(capturedDay);
                }
                else
                {
                    _selectedDays.Remove(capturedDay);
                }

                RefreshScheduleControls();
            };

            _dayToggleButtons[day] = button;
            _dayTogglePanel.Children.Add(button);
        }

        panel.Children.Add(editDaysRow);
        panel.Children.Add(_editingDaysTextBlock);
        panel.Children.Add(CreateInlineLabel("Work windows for selected days:"));
        panel.Children.Add(_workWindowRowsPanel);

        var windowActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                _addWindowButton,
                _removeLastWindowButton,
            }
        };
        _addWindowButton.Click += (_, _) => AddWindowToSelectedDays();
        _removeLastWindowButton.Click += (_, _) => RemoveLastWindowFromSelectedDays();
        panel.Children.Add(windowActions);

        panel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 2),
        });

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                CreateInlineLabel("Actions:"),
                _copySourceComboBox,
                new Button
                {
                    Content = "Copy to selected days",
                    MinWidth = 150,
                },
                new Button
                {
                    Content = "Clear selected days",
                    MinWidth = 145,
                },
                new Button
                {
                    Content = "Reset defaults",
                    MinWidth = 115,
                },
            }
        };
        ((Button)actionRow.Children[2]!).Click += (_, _) =>
        {
            if (_copySourceComboBox.SelectedItem is DayChoice choice)
            {
                CopyDayToSelectedDays(choice.Day);
            }
        };
        ((Button)actionRow.Children[3]!).Click += (_, _) => ClearSelectedDays();
        ((Button)actionRow.Children[4]!).Click += (_, _) => ResetUsageConfigurationToDefaults();
        panel.Children.Add(actionRow);
        panel.Children.Add(_scheduleActionTextBlock);
        panel.Children.Add(_weeklyHoursTextBlock);
        panel.Children.Add(_zeroWeeklyHoursTextBlock);

        return CreateSectionBorder(panel);
    }

    private Control CreateShortcutRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                CreateInlineLabel("Quick select:"),
            }
        };

        row.Children.Add(CreateShortcutButton("Weekdays", Weekdays));
        row.Children.Add(CreateShortcutButton("Weekend", Weekend));
        row.Children.Add(CreateShortcutButton("All days", OrderedDays));
        return row;
    }

    private Button CreateShortcutButton(string text, IReadOnlyCollection<DayOfWeek> days)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 88,
        };
        button.Click += (_, _) => SetSelectedDays(days);
        return button;
    }

    private Control BuildThresholdSection()
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateSectionTitle("Quota color thresholds"),
                CreateHintText("Percentages use the same gates as today. Change values to customize thresholds."),
            }
        };

        var inputGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 14,
        };
        inputGrid.Children.Add(CreateThresholdField("Red below (%)", _redThresholdInput, 0));
        inputGrid.Children.Add(CreateThresholdField("Yellow below (%)", _yellowThresholdInput, 1));
        inputGrid.Children.Add(CreateThresholdField("Blue above (%)", _blueThresholdInput, 2));
        inputGrid.Children.Add(CreateThresholdField("Pink above (%)", _pinkThresholdInput, 3));
        panel.Children.Add(inputGrid);
        panel.Children.Add(_thresholdValidationTextBlock);
        panel.Children.Add(new Button
        {
            Content = "Reset thresholds to defaults",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 180,
        });
        ((Button)panel.Children[^1]!).Click += (_, _) => ResetThresholdsToDefaults();

        return CreateSectionBorder(panel);
    }

    private Control CreateThresholdField(string label, NumericUpDown input, int column)
    {
        input.ValueChanged += (_, _) =>
        {
            if (!_isRefreshingControls)
            {
                RefreshValidationState();
            }
        };

        var stack = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                CreateInlineLabel(label),
                input,
            }
        };
        Grid.SetColumn(stack, column);
        return stack;
    }

    private Border CreateSectionBorder(Control child)
    {
        var border = new Border
        {
            Padding = new Thickness(14),
            Margin = new Thickness(0, 8, 0, 0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = child,
        };
        _sectionBorders.Add(border);
        return border;
    }

    private TextBlock CreateSectionTitle(string text)
    {
        var title = new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
        };
        _fieldLabels.Add(title);
        return title;
    }

    private TextBlock CreateInlineLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _fieldLabels.Add(label);
        return label;
    }

    private static NumericUpDown CreateNumericInput(int minimum, int maximum, int increment)
    {
        return new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            ClipValueToMinMax = true,
            ShowButtonSpinner = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FormatString = "0",
            Width = 200,
        };
    }

    private static NumericUpDown CreateThresholdInput()
    {
        return new NumericUpDown
        {
            Minimum = 0,
            Maximum = 1000,
            Increment = 1,
            ClipValueToMinMax = true,
            ShowButtonSpinner = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            FormatString = "0",
            Width = 120,
        };
    }

    internal static void ConfigureThemeSelector(ComboBox comboBox)
    {
        ArgumentNullException.ThrowIfNull(comboBox);

        comboBox.ItemsSource = new[] { WidgetThemePreference.Light, WidgetThemePreference.Dark };
        comboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        comboBox.MinWidth = 180;
        AutomationProperties.SetName(comboBox, ThemeSelectorAutomationName);
    }

    private Control CreateFieldRow(string label, Control control)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = FieldRowWidth,
        };
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 270,
        };
        _fieldLabels.Add(labelBlock);
        row.Children.Add(labelBlock);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private TextBlock CreateHintText(string text)
    {
        var hint = new TextBlock
        {
            Text = text,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };
        _hintTextBlocks.Add(hint);
        return hint;
    }

    private WidgetPreferenceDraft BuildDraftFromControls()
    {
        var selectedView = _selectedViewComboBox.SelectedItem is WidgetViewKind selected
            ? selected
            : WidgetPreferenceDefaults.DefaultSelectedView;

        return new WidgetPreferenceDraft
        {
            SelectedView = selectedView,
            WidgetScalePercent = ReadInt(_widgetScaleInput, WidgetPreferenceDefaults.DefaultWidgetScalePercent),
            AlwaysOnTop = _alwaysOnTopCheckBox.IsChecked ?? WidgetPreferenceDefaults.DefaultAlwaysOnTop,
            RefreshPeriodSeconds = ReadInt(_refreshPeriodInput, WidgetPreferenceDefaults.DefaultRefreshPeriodSeconds),
            Theme = ResolveSelectedTheme(),
            WorkSchedule = BuildWorkScheduleFromEditor(),
            QuotaThresholds = BuildQuotaThresholdsFromControls(),
        };
    }

    private void ApplyDraftToControls(WidgetPreferenceDraft draft)
    {
        _isRefreshingControls = true;
        try
        {
            _selectedViewComboBox.SelectedItem = draft.SelectedView;
            _themeComboBox.SelectedItem = Enum.IsDefined(draft.Theme)
                ? draft.Theme
                : WidgetPreferenceDefaults.DefaultTheme;
            _widgetScaleInput.Value = draft.WidgetScalePercent;
            _alwaysOnTopCheckBox.IsChecked = draft.AlwaysOnTop;
            _refreshPeriodInput.Value = draft.RefreshPeriodSeconds;
            _redThresholdInput.Value = draft.QuotaThresholds.RedBelowPercent;
            _yellowThresholdInput.Value = draft.QuotaThresholds.YellowBelowPercent;
            _blueThresholdInput.Value = draft.QuotaThresholds.BlueAbovePercent;
            _pinkThresholdInput.Value = draft.QuotaThresholds.PinkAbovePercent;
            LoadScheduleRows(draft.WorkSchedule);
            _selectedDays.Clear();
            foreach (var day in Weekdays)
            {
                _selectedDays.Add(day);
            }

            _scheduleActionTextBlock.Text = string.Empty;
        }
        finally
        {
            _isRefreshingControls = false;
        }

        RefreshScheduleControls();
        ApplyThemeStyles(ResolveSelectedTheme());
    }

    private void LoadScheduleRows(WeeklyWorkSchedule schedule)
    {
        _workScheduleRows.Clear();
        foreach (var (day, rows) in CreateScheduleRows(schedule))
        {
            _workScheduleRows[day] = rows;
        }
    }

    private void RefreshScheduleControls()
    {
        _isRefreshingControls = true;
        try
        {
            foreach (var (day, button) in _dayToggleButtons)
            {
                button.IsChecked = _selectedDays.Contains(day);
            }

            _editingDaysTextBlock.Text = FormatEditingDaysText(_selectedDays);
            RebuildWorkWindowRows();
            RefreshWindowActionAvailability();
        }
        finally
        {
            _isRefreshingControls = false;
        }

        RefreshValidationState();
    }

    private void RebuildWorkWindowRows()
    {
        _workWindowRowsPanel.Children.Clear();

        if (_selectedDays.Count == 0)
        {
            _workWindowRowsPanel.Children.Add(CreateWindowTableRowBorder(new TextBlock
            {
                Text = "No days selected. Select one or more days to edit work windows.",
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 14),
            }, isHeader: false));
            return;
        }

        var selectionState = BuildSelectionScheduleState(_workScheduleRows, _selectedDays);
        if (selectionState.HasMixedSchedules)
        {
            _workWindowRowsPanel.Children.Add(CreateWindowTableRowBorder(new TextBlock
            {
                Text = selectionState.DifferenceText,
                FontSize = 12,
                TextAlignment = TextAlignment.Left,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 14),
            }, isHeader: false));
            return;
        }

        var selectedRows = GetSelectedRows();
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("55,*,*,90"),
            ColumnSpacing = 10,
            MinHeight = 28,
            Children =
            {
                CreateInlineLabel("#"),
                CreateInlineLabel("Start (HH:mm)"),
                CreateInlineLabel("End (HH:mm)"),
            },
        };
        Grid.SetColumn(header.Children[1]!, 1);
        Grid.SetColumn(header.Children[2]!, 2);
        _workWindowRowsPanel.Children.Add(CreateWindowTableRowBorder(header, isHeader: true));

        if (selectedRows.Count == 0)
        {
            _workWindowRowsPanel.Children.Add(CreateWindowTableRowBorder(new TextBlock
            {
                Text = "No work windows configured. Add one or more time windows to define your work schedule.",
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 14),
            }, isHeader: false));
            return;
        }

        for (var index = 0; index < selectedRows.Count; index++)
        {
            _workWindowRowsPanel.Children.Add(CreateWorkWindowRow(index, selectedRows[index]));
        }
    }

    private Border CreateWorkWindowRow(int index, WorkWindowEditorRow row)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("55,*,*,90"),
            ColumnSpacing = 10,
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            MinHeight = 40,
        };

        var number = CreateInlineLabel((index + 1).ToString(CultureInfo.InvariantCulture));
        number.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(number);

        var startBox = CreateTimeTextBox(row.StartText);
        startBox.TextChanged += (_, _) =>
        {
            if (_isRefreshingControls)
            {
                return;
            }

            SetSelectedRowText(index, isStart: true, startBox.Text ?? string.Empty);
            RefreshValidationState();
        };
        Grid.SetColumn(startBox, 1);
        grid.Children.Add(startBox);

        var endBox = CreateTimeTextBox(row.EndText);
        endBox.TextChanged += (_, _) =>
        {
            if (_isRefreshingControls)
            {
                return;
            }

            SetSelectedRowText(index, isStart: false, endBox.Text ?? string.Empty);
            RefreshValidationState();
        };
        Grid.SetColumn(endBox, 2);
        grid.Children.Add(endBox);

        var removeButton = new Button
        {
            Content = "Remove",
            MinWidth = 76,
        };
        removeButton.Click += (_, _) => RemoveWindowFromSelectedDays(index);
        Grid.SetColumn(removeButton, 3);
        grid.Children.Add(removeButton);

        var error = CreateHintText(string.Empty);
        error.IsVisible = false;
        error.Tag = $"schedule-error-{index}";
        Grid.SetRow(error, 1);
        Grid.SetColumn(error, 1);
        Grid.SetColumnSpan(error, 3);
        grid.Children.Add(error);

        return CreateWindowTableRowBorder(grid, isHeader: false);
    }

    private TextBox CreateTimeTextBox(string text)
    {
        return new TextBox
        {
            Text = text,
            PlaceholderText = "HH:mm",
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    private Border CreateWindowTableRowBorder(Control child, bool isHeader)
    {
        return new Border
        {
            BorderThickness = new Thickness(1, isHeader ? 1 : 0, 1, 1),
            Padding = new Thickness(8, 4),
            Child = child,
        };
    }

    private void AddWindowToSelectedDays()
    {
        if (_selectedDays.Count == 0)
        {
            _scheduleActionTextBlock.Text = "Select one or more days before editing work windows.";
            return;
        }

        if (SelectedSchedulesDiffer())
        {
            _scheduleActionTextBlock.Text = "Schedules differ among selected days. Use Copy to selected days to equalize schedules before editing windows directly.";
            return;
        }

        var rows = GetSelectedRows();
        rows.Add(new WorkWindowEditorRow("09:00", "17:00"));
        SetRowsForSelectedDays(rows);
        _scheduleActionTextBlock.Text = $"Added a work window for {FormatDayList(_selectedDays)}.";
    }

    private void RemoveLastWindowFromSelectedDays()
    {
        if (_selectedDays.Count == 0)
        {
            _scheduleActionTextBlock.Text = "Select one or more days before editing work windows.";
            return;
        }

        if (SelectedSchedulesDiffer())
        {
            _scheduleActionTextBlock.Text = "Schedules differ among selected days. Use Copy to selected days to equalize schedules before editing windows directly.";
            return;
        }

        var rows = GetSelectedRows();
        if (rows.Count > 0)
        {
            rows.RemoveAt(rows.Count - 1);
            SetRowsForSelectedDays(rows);
            _scheduleActionTextBlock.Text = $"Removed the last work window from {FormatDayList(_selectedDays)}.";
        }
    }

    private void RemoveWindowFromSelectedDays(int rowIndex)
    {
        if (_selectedDays.Count == 0)
        {
            _scheduleActionTextBlock.Text = "Select one or more days before editing work windows.";
            return;
        }

        if (SelectedSchedulesDiffer())
        {
            _scheduleActionTextBlock.Text = "Schedules differ among selected days. Use Copy to selected days to equalize schedules before editing windows directly.";
            return;
        }

        var rows = GetSelectedRows();
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            return;
        }

        rows.RemoveAt(rowIndex);
        SetRowsForSelectedDays(rows);
        _scheduleActionTextBlock.Text = $"Removed window {rowIndex + 1} from {FormatDayList(_selectedDays)}.";
    }

    private void ClearSelectedDays()
    {
        if (_selectedDays.Count == 0)
        {
            _scheduleActionTextBlock.Text = "Select one or more days before editing work windows.";
            return;
        }

        SetRowsForSelectedDays([]);
        _scheduleActionTextBlock.Text = $"Cleared work windows for {FormatDayList(_selectedDays)}.";
    }

    private void CopyDayToSelectedDays(DayOfWeek sourceDay)
    {
        if (_selectedDays.Count == 0)
        {
            _scheduleActionTextBlock.Text = "Select one or more days before editing work windows.";
            return;
        }

        var sourceRows = CloneRows(_workScheduleRows[sourceDay]);
        SetRowsForSelectedDays(sourceRows);
        _scheduleActionTextBlock.Text = $"{FormatLongDay(sourceDay)}'s schedule will be applied to {FormatDayList(_selectedDays)} when you save.";
    }

    private void ResetUsageConfigurationToDefaults()
    {
        LoadScheduleRows(UsageConfigurationDefaults.CreateDefaultWeeklyWorkSchedule());
        ResetThresholdsToDefaults();
        RefreshScheduleControls();
        _scheduleActionTextBlock.Text = "Schedule and quota thresholds were reset to defaults.";
    }

    private void ResetThresholdsToDefaults()
    {
        var defaults = UsageConfigurationDefaults.CreateDefaultQuotaThresholds();
        _redThresholdInput.Value = defaults.RedBelowPercent;
        _yellowThresholdInput.Value = defaults.YellowBelowPercent;
        _blueThresholdInput.Value = defaults.BlueAbovePercent;
        _pinkThresholdInput.Value = defaults.PinkAbovePercent;
        RefreshValidationState();
    }

    private void SetSelectedDays(IEnumerable<DayOfWeek> days)
    {
        _selectedDays.Clear();
        foreach (var day in NormalizeSelectedDays(days))
        {
            _selectedDays.Add(day);
        }

        RefreshScheduleControls();
    }

    private void SetRowsForSelectedDays(IEnumerable<WorkWindowEditorRow> rows)
    {
        if (_selectedDays.Count == 0)
        {
            RefreshScheduleControls();
            return;
        }

        var rowSnapshot = CloneRows(rows);
        foreach (var day in _selectedDays)
        {
            _workScheduleRows[day] = CloneRows(rowSnapshot);
        }

        RefreshScheduleControls();
    }

    private void SetSelectedRowText(int rowIndex, bool isStart, string text)
    {
        if (_isRefreshingControls || _selectedDays.Count == 0)
        {
            return;
        }

        foreach (var day in _selectedDays)
        {
            var rows = _workScheduleRows[day];
            if (rowIndex < 0 || rowIndex >= rows.Count)
            {
                continue;
            }

            if (isStart)
            {
                rows[rowIndex].StartText = text;
            }
            else
            {
                rows[rowIndex].EndText = text;
            }
        }
    }

    private List<WorkWindowEditorRow> GetSelectedRows()
    {
        return GetSelectedRows(_workScheduleRows, _selectedDays);
    }

    private void RefreshWindowActionAvailability()
    {
        var canEditWindows = BuildSelectionScheduleState(_workScheduleRows, _selectedDays).CanEditWindows;
        _addWindowButton.IsEnabled = canEditWindows;
        _removeLastWindowButton.IsEnabled = canEditWindows;
    }

    private bool SelectedSchedulesDiffer()
    {
        return BuildSelectionScheduleState(_workScheduleRows, _selectedDays).HasMixedSchedules;
    }

    private WeeklyWorkSchedule BuildWorkScheduleFromEditor()
    {
        return BuildWorkSchedule(_workScheduleRows);
    }

    private static WeeklyWorkSchedule BuildWorkSchedule(IReadOnlyDictionary<DayOfWeek, List<WorkWindowEditorRow>> rowsByDay)
    {
        return new WeeklyWorkSchedule
        {
            Monday = BuildDaySchedule(rowsByDay[DayOfWeek.Monday]),
            Tuesday = BuildDaySchedule(rowsByDay[DayOfWeek.Tuesday]),
            Wednesday = BuildDaySchedule(rowsByDay[DayOfWeek.Wednesday]),
            Thursday = BuildDaySchedule(rowsByDay[DayOfWeek.Thursday]),
            Friday = BuildDaySchedule(rowsByDay[DayOfWeek.Friday]),
            Saturday = BuildDaySchedule(rowsByDay[DayOfWeek.Saturday]),
            Sunday = BuildDaySchedule(rowsByDay[DayOfWeek.Sunday]),
        };
    }

    private static Dictionary<DayOfWeek, List<WorkWindowEditorRow>> CreateScheduleRows(WeeklyWorkSchedule schedule)
    {
        var rowsByDay = new Dictionary<DayOfWeek, List<WorkWindowEditorRow>>();
        foreach (var day in OrderedDays)
        {
            rowsByDay[day] = schedule.GetDaySchedule(day).Windows
                .Select(window => new WorkWindowEditorRow(FormatTime(window.Start), FormatTime(window.End)))
                .ToList();
        }

        return rowsByDay;
    }

    private static HashSet<DayOfWeek> NormalizeSelectedDays(IEnumerable<DayOfWeek> days)
    {
        return days
            .Where(OrderedDays.Contains)
            .ToHashSet();
    }

    private static List<WorkWindowEditorRow> GetSelectedRows(
        IReadOnlyDictionary<DayOfWeek, List<WorkWindowEditorRow>> rowsByDay,
        IReadOnlySet<DayOfWeek> selectedDays)
    {
        return selectedDays.Count == 0
            ? []
            : CloneRows(rowsByDay[OrderedDays.First(selectedDays.Contains)]);
    }

    private static SelectionScheduleState BuildSelectionScheduleState(
        IReadOnlyDictionary<DayOfWeek, List<WorkWindowEditorRow>> rowsByDay,
        IReadOnlySet<DayOfWeek> selectedDays)
    {
        if (selectedDays.Count == 0)
        {
            return new SelectionScheduleState(false, false, string.Empty);
        }

        var groups = BuildSelectedScheduleGroups(rowsByDay, selectedDays);
        if (groups.Count <= 1)
        {
            return new SelectionScheduleState(false, true, string.Empty);
        }

        return new SelectionScheduleState(
            true,
            false,
            FormatScheduleDifferenceText(groups, groups[0].Days[0]));
    }

    private static List<ScheduleDifferenceGroup> BuildSelectedScheduleGroups(
        IReadOnlyDictionary<DayOfWeek, List<WorkWindowEditorRow>> rowsByDay,
        IReadOnlySet<DayOfWeek> selectedDays)
    {
        var groups = new List<ScheduleDifferenceGroup>();
        foreach (var day in OrderedDays.Where(selectedDays.Contains))
        {
            var schedule = BuildDaySchedule(rowsByDay[day]);
            var existingGroup = groups.FirstOrDefault(group => group.Schedule.Equals(schedule));
            if (existingGroup is not null)
            {
                existingGroup.Days.Add(day);
                continue;
            }

            groups.Add(new ScheduleDifferenceGroup(schedule, [day]));
        }

        return groups;
    }

    private static WeeklyWorkSchedule BuildScheduleFromDayResolver(Func<DayOfWeek, DayWorkSchedule> resolveDay)
    {
        return new WeeklyWorkSchedule
        {
            Monday = resolveDay(DayOfWeek.Monday),
            Tuesday = resolveDay(DayOfWeek.Tuesday),
            Wednesday = resolveDay(DayOfWeek.Wednesday),
            Thursday = resolveDay(DayOfWeek.Thursday),
            Friday = resolveDay(DayOfWeek.Friday),
            Saturday = resolveDay(DayOfWeek.Saturday),
            Sunday = resolveDay(DayOfWeek.Sunday),
        };
    }

    private static DayWorkSchedule BuildDaySchedule(IEnumerable<WorkWindowEditorRow> rows)
    {
        return new DayWorkSchedule
        {
            Windows = rows
                .Select(row => TryBuildWorkWindow(row, out var window) ? window : null)
                .OfType<WorkWindow>()
                .ToArray(),
        };
    }

    private QuotaThresholds BuildQuotaThresholdsFromControls()
    {
        return new QuotaThresholds
        {
            RedBelowPercent = ReadInt(_redThresholdInput, UsageConfigurationDefaults.DefaultRedBelowPercent),
            YellowBelowPercent = ReadInt(_yellowThresholdInput, UsageConfigurationDefaults.DefaultYellowBelowPercent),
            BlueAbovePercent = ReadInt(_blueThresholdInput, UsageConfigurationDefaults.DefaultBlueAbovePercent),
            PinkAbovePercent = ReadInt(_pinkThresholdInput, UsageConfigurationDefaults.DefaultPinkAbovePercent),
        };
    }

    private void RefreshValidationState()
    {
        var palette = WidgetVisualStyles.ResolvePalette(ResolveSelectedTheme());
        var validation = ValidateEditor();
        _saveButton.IsEnabled = validation.IsValid;
        _weeklyHoursTextBlock.Text = FormatWeeklyHours(validation.Schedule);
        _zeroWeeklyHoursTextBlock.IsVisible = UsageConfigurationRules.GetTotalWeeklyMinutes(validation.Schedule) == 0;
        _thresholdValidationTextBlock.Text = validation.ThresholdIssues.Count == 0
            ? "Values must be in ascending order: Red < Yellow < Blue < Pink."
            : string.Join(" ", validation.ThresholdIssues.Select(issue => issue.Message));
        _thresholdValidationTextBlock.Foreground = validation.ThresholdIssues.Count == 0
            ? palette.MutedTextBrush
            : palette.ErrorTextBrush;

        _statusTextBlock.Foreground = validation.IsValid ? palette.MutedTextBrush : palette.ErrorTextBrush;
        _statusTextBlock.Text = validation.IsValid
            ? _statusTextBlock.Text == "Fix validation errors before saving." ? string.Empty : _statusTextBlock.Text
            : "Fix validation errors before saving.";

        ApplyRowValidationMessages(validation.ScheduleIssues, validation.InvalidTimeRows, palette);
    }

    private SettingsWindowEditorValidation ValidateEditor()
    {
        var invalidTimeRows = new Dictionary<int, string>();
        var selectedRows = GetSelectedRows();
        for (var index = 0; index < selectedRows.Count; index++)
        {
            if (!TryParseTime(selectedRows[index].StartText, out _)
                || !TryParseTime(selectedRows[index].EndText, out _))
            {
                invalidTimeRows[index] = "Use HH:mm times such as 07:00 or 17:30.";
            }
        }

        var schedule = BuildWorkScheduleFromEditor();
        var scheduleIssues = UsageConfigurationRules.ValidateWeeklyWorkSchedule(schedule);
        var thresholdIssues = UsageConfigurationRules.ValidateQuotaThresholds(BuildQuotaThresholdsFromControls());
        return new SettingsWindowEditorValidation(
            schedule,
            scheduleIssues,
            thresholdIssues,
            invalidTimeRows);
    }

    private void ApplyRowValidationMessages(
        IReadOnlyList<UsageConfigurationIssue> scheduleIssues,
        IReadOnlyDictionary<int, string> invalidTimeRows,
        WidgetThemePalette palette)
    {
        var selectedDay = OrderedDays.FirstOrDefault(_selectedDays.Contains);
        var selectedDayName = ToIssueDayName(selectedDay);
        var messagesBySelectedRow = new Dictionary<int, string>(invalidTimeRows);

        foreach (var issue in scheduleIssues)
        {
            var rowIndex = TryParseIssueRowIndex(issue.Path, selectedDayName);
            if (rowIndex is not null)
            {
                messagesBySelectedRow[rowIndex.Value] = issue.Message;
            }
        }

        foreach (var rowBorder in _workWindowRowsPanel.Children.OfType<Border>())
        {
            if (rowBorder.Child is not Grid grid)
            {
                continue;
            }

            foreach (var error in grid.Children.OfType<TextBlock>().Where(child => child.Tag is string tag && tag.StartsWith("schedule-error-", StringComparison.Ordinal)))
            {
                var tag = (string)error.Tag!;
                var rowIndex = int.Parse(tag[15..], CultureInfo.InvariantCulture);
                if (messagesBySelectedRow.TryGetValue(rowIndex, out var message))
                {
                    ApplyRowValidationMessage(rowBorder, error, message, palette);
                }
                else
                {
                    ApplyRowValidationMessage(rowBorder, error, null, palette);
                }
            }
        }
    }

    private static void ApplyRowValidationMessage(
        Border rowBorder,
        TextBlock error,
        string? message,
        WidgetThemePalette palette)
    {
        if (!string.IsNullOrEmpty(message))
        {
            error.Text = message;
            error.Foreground = palette.ErrorTextBrush;
            error.IsVisible = true;
            rowBorder.BorderBrush = palette.ErrorTextBrush;
            return;
        }

        error.Text = string.Empty;
        error.IsVisible = false;
        rowBorder.BorderBrush = palette.SettingsBorderBrush;
    }

    private void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RefreshValidationState();
        if (!_saveButton.IsEnabled)
        {
            return;
        }

        var outcome = _preferenceCoordinator.SaveAndApply(BuildDraftFromControls());
        if (outcome.AppliedPreferences is not null)
        {
            _preferenceCoordinator.UpdateCurrentPreferences(outcome.AppliedPreferences);
            ApplyDraftToControls(_preferenceCoordinator.CreateDraft());
        }

        var palette = WidgetVisualStyles.ResolvePalette(ResolveSelectedTheme());
        _statusTextBlock.Foreground = outcome.Succeeded ? palette.SuccessTextBrush : palette.ErrorTextBrush;
        _statusTextBlock.Text = outcome.Messages.Count == 0
            ? (outcome.Succeeded ? "Preferences saved." : "Preferences could not be saved.")
            : string.Join(" ", outcome.Messages);
    }

    private WidgetThemePreference ResolveSelectedTheme()
    {
        return _themeComboBox.SelectedItem is WidgetThemePreference theme && Enum.IsDefined(theme)
            ? theme
            : WidgetPreferenceDefaults.DefaultTheme;
    }

    private void ApplyThemeStyles(WidgetThemePreference theme)
    {
        var palette = WidgetVisualStyles.ResolvePalette(theme);
        Background = palette.SettingsSurfaceBrush;
        _contentBorder.Background = palette.SettingsPanelBrush;
        _contentBorder.BorderBrush = palette.SettingsBorderBrush;
        _contentBorder.BorderThickness = new Thickness(1);
        _contentBorder.CornerRadius = new CornerRadius(6);
        _statusTextBlock.Foreground = palette.MutedTextBrush;

        foreach (var label in _fieldLabels)
        {
            label.Foreground = palette.PrimaryTextBrush;
        }

        foreach (var hint in _hintTextBlocks)
        {
            hint.Foreground = ReferenceEquals(hint, _thresholdValidationTextBlock) && !string.IsNullOrWhiteSpace(hint.Text) && !_saveButton.IsEnabled
                ? palette.ErrorTextBrush
                : palette.MutedTextBrush;
        }

        foreach (var section in _sectionBorders)
        {
            section.BorderBrush = palette.SettingsBorderBrush;
        }

        foreach (var row in _workWindowRowsPanel.Children.OfType<Border>())
        {
            row.BorderBrush = palette.SettingsBorderBrush;
        }

        foreach (var (day, button) in _dayToggleButtons)
        {
            if (_selectedDays.Contains(day))
            {
                button.Background = palette.ActiveBadgeBackgroundBrush;
                button.Foreground = palette.ActiveTextBrush;
                button.BorderBrush = palette.ActiveDotBrush;
            }
            else
            {
                button.ClearValue(BackgroundProperty);
                button.Foreground = palette.PrimaryTextBrush;
                button.BorderBrush = palette.SettingsBorderBrush;
            }
        }

        RefreshValidationState();
    }

    private static int ReadInt(NumericUpDown input, int fallback)
    {
        if (input.Value is null)
        {
            return fallback;
        }

        return decimal.ToInt32(decimal.Round(input.Value.Value, 0, MidpointRounding.AwayFromZero));
    }

    private static bool TryBuildWorkWindow(WorkWindowEditorRow row, out WorkWindow? window)
    {
        if (TryParseTime(row.StartText, out var start) && TryParseTime(row.EndText, out var end))
        {
            window = new WorkWindow
            {
                Start = start,
                End = end,
            };
            return true;
        }

        window = null;
        return false;
    }

    private static bool TryParseTime(string? text, out TimeOnly time)
    {
        return TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    private static string FormatTime(TimeOnly time)
    {
        return time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static List<WorkWindowEditorRow> CloneRows(IEnumerable<WorkWindowEditorRow> rows)
    {
        return rows.Select(row => new WorkWindowEditorRow(row.StartText, row.EndText)).ToList();
    }

    private static string FormatWeeklyHours(WeeklyWorkSchedule schedule)
    {
        var weekdayMinutes = Weekdays.Sum(day => schedule.GetDaySchedule(day).Windows.Sum(window => Math.Max(0, window.DurationMinutes)));
        var weekendMinutes = Weekend.Sum(day => schedule.GetDaySchedule(day).Windows.Sum(window => Math.Max(0, window.DurationMinutes)));
        var totalMinutes = weekdayMinutes + weekendMinutes;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Total configured weekly hours: {totalMinutes / 60d:0.00} h   (Mon-Fri: {weekdayMinutes / 60d:0.00} h, Sat-Sun: {weekendMinutes / 60d:0.00} h)");
    }

    private static string FormatScheduleDifferenceText(IReadOnlyList<ScheduleDifferenceGroup> groups, DayOfWeek copySourceDay)
    {
        var groupText = string.Join(
            Environment.NewLine,
            groups.Select(group => $"{FormatDayList(group.Days)}: {FormatDaySchedule(group.Schedule)}"));
        return $"Schedules differ among selected days.{Environment.NewLine}{groupText}{Environment.NewLine}Use Copy {FormatLongDay(copySourceDay)} to selected days to equalize schedules before editing windows directly.";
    }

    private static string FormatDaySchedule(DayWorkSchedule schedule)
    {
        return schedule.Windows.Length == 0
            ? "no work windows"
            : string.Join(", ", schedule.Windows.Select(window => $"{FormatTime(window.Start)}-{FormatTime(window.End)}"));
    }

    private static int? TryParseIssueRowIndex(string path, string selectedDayName)
    {
        if (!path.StartsWith(selectedDayName + "[", StringComparison.Ordinal))
        {
            return null;
        }

        var start = selectedDayName.Length + 1;
        var end = path.IndexOf(']', start);
        if (end <= start)
        {
            return null;
        }

        return int.TryParse(path[start..end], CultureInfo.InvariantCulture, out var rowIndex)
            ? rowIndex
            : null;
    }

    private static string ToIssueDayName(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => "monday",
            DayOfWeek.Tuesday => "tuesday",
            DayOfWeek.Wednesday => "wednesday",
            DayOfWeek.Thursday => "thursday",
            DayOfWeek.Friday => "friday",
            DayOfWeek.Saturday => "saturday",
            DayOfWeek.Sunday => "sunday",
            _ => string.Empty,
        };
    }

    private static string FormatDayList(IEnumerable<DayOfWeek> days)
    {
        return string.Join(", ", OrderedDays.Where(days.Contains).Select(FormatShortDay));
    }

    private static string FormatEditingDaysText(IReadOnlySet<DayOfWeek> days)
    {
        return days.Count == 0
            ? "Editing: no days selected"
            : $"Editing: {FormatDayList(days)}";
    }

    private static string FormatShortDay(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => "Mon",
            DayOfWeek.Tuesday => "Tue",
            DayOfWeek.Wednesday => "Wed",
            DayOfWeek.Thursday => "Thu",
            DayOfWeek.Friday => "Fri",
            DayOfWeek.Saturday => "Sat",
            DayOfWeek.Sunday => "Sun",
            _ => day.ToString(),
        };
    }

    private static string FormatLongDay(DayOfWeek day)
    {
        return day.ToString();
    }

    private sealed record DayChoice(DayOfWeek Day)
    {
        public override string ToString()
        {
            return $"Copy {FormatLongDay(Day)} to selected days";
        }
    }

    private sealed class WorkWindowEditorRow(string startText, string endText)
    {
        public string StartText { get; set; } = startText;

        public string EndText { get; set; } = endText;
    }
}

internal sealed record SettingsWindowEditorSnapshot(
    WeeklyWorkSchedule WorkSchedule,
    QuotaThresholds QuotaThresholds,
    bool CanSave,
    string WeeklyHoursText,
    string ThresholdValidationText,
    IReadOnlyList<(string Start, string End)> SelectedRows,
    string EditingDaysText,
    string ScheduleActionText,
    bool CanAddWindow,
    bool CanRemoveLastWindow,
    string ScheduleDifferenceText);

internal sealed record SettingsWindowSelectionPreview(
    WeeklyWorkSchedule WorkSchedule,
    IReadOnlyList<(string Start, string End)> SelectedRows,
    string EditingDaysText,
    bool CanEditWindows,
    bool HasMixedSchedules,
    string ScheduleDifferenceText);

internal sealed record SettingsWindowEditorValidation(
    WeeklyWorkSchedule Schedule,
    IReadOnlyList<UsageConfigurationIssue> ScheduleIssues,
    IReadOnlyList<UsageConfigurationIssue> ThresholdIssues,
    IReadOnlyDictionary<int, string> InvalidTimeRows)
{
    public bool IsValid => ScheduleIssues.Count == 0 && ThresholdIssues.Count == 0 && InvalidTimeRows.Count == 0;
}

internal sealed record SettingsWindowRowValidationPresentation(
    string ErrorText,
    bool IsErrorVisible,
    bool UsesErrorBorder);

internal sealed record SelectionScheduleState(
    bool HasMixedSchedules,
    bool CanEditWindows,
    string DifferenceText);

internal sealed record ScheduleDifferenceGroup(
    DayWorkSchedule Schedule,
    List<DayOfWeek> Days);
