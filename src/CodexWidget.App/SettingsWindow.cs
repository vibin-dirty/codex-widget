using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexWidget.Core;

namespace CodexWidget.App;

internal sealed class SettingsWindow : Window
{
    internal const string ThemeSelectorAutomationName = "Theme";

    private readonly WidgetPreferenceCoordinator _preferenceCoordinator;
    private readonly ComboBox _selectedViewComboBox;
    private readonly ComboBox _themeComboBox;
    private readonly NumericUpDown _widgetScaleInput;
    private readonly CheckBox _alwaysOnTopCheckBox;
    private readonly NumericUpDown _refreshPeriodInput;
    private readonly TextBlock _statusTextBlock;
    private readonly Border _contentBorder;
    private readonly List<TextBlock> _fieldLabels = [];
    private readonly List<TextBlock> _hintTextBlocks = [];

    public SettingsWindow(WindowIcon? icon, WidgetPreferenceCoordinator preferenceCoordinator)
    {
        _preferenceCoordinator = preferenceCoordinator ?? throw new ArgumentNullException(nameof(preferenceCoordinator));

        Title = "Settings";
        Icon = icon;
        Width = 420;
        Height = 400;
        MinWidth = 420;
        MinHeight = 400;
        MaxWidth = 420;
        MaxHeight = 400;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _selectedViewComboBox = new ComboBox
        {
            ItemsSource = new[] { WidgetViewKind.Minimal, WidgetViewKind.Compact, WidgetViewKind.Full },
            SelectedIndex = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _themeComboBox = new ComboBox
        {
            SelectedIndex = 0,
        };
        ConfigureThemeSelector(_themeComboBox);
        _alwaysOnTopCheckBox = new CheckBox
        {
            Content = "Keep widget above other windows",
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
            MaxWidth = 380,
        };

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

    private Control BuildContent()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 12,
        };

        var fields = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateFieldRow("Selected view", _selectedViewComboBox),
                CreateFieldRow("Theme", _themeComboBox),
                CreateFieldRow("Always on top", _alwaysOnTopCheckBox),
                CreateFieldRow("Widget scale (%)", _widgetScaleInput),
                CreateFieldRow("Refresh period (seconds)", _refreshPeriodInput),
                CreateHintText($"Scale range: {WidgetPreferenceDefaults.MinimumWidgetScalePercent}% to {WidgetPreferenceDefaults.MaximumWidgetScalePercent}% in {WidgetPreferenceDefaults.WidgetScaleStepPercent}% steps."),
                CreateHintText($"Allowed range: {WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds} to {WidgetPreferenceDefaults.MaximumRefreshPeriodSeconds} seconds."),
                _statusTextBlock,
            }
        };
        Grid.SetRow(fields, 0);
        root.Children.Add(fields);

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
                new Button
                {
                    Content = "Save",
                    MinWidth = 90,
                },
            }
        };

        var cancelButton = (Button)buttons.Children[0]!;
        cancelButton.Click += (_, _) => Close();
        var saveButton = (Button)buttons.Children[1]!;
        saveButton.Click += OnSaveClicked;

        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        return root;
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
            Width = 180,
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
        };
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 190,
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
        };
    }

    private void ApplyDraftToControls(WidgetPreferenceDraft draft)
    {
        _selectedViewComboBox.SelectedItem = draft.SelectedView;
        _themeComboBox.SelectedItem = Enum.IsDefined(draft.Theme)
            ? draft.Theme
            : WidgetPreferenceDefaults.DefaultTheme;
        _widgetScaleInput.Value = draft.WidgetScalePercent;
        _alwaysOnTopCheckBox.IsChecked = draft.AlwaysOnTop;
        _refreshPeriodInput.Value = draft.RefreshPeriodSeconds;
        ApplyThemeStyles(ResolveSelectedTheme());
    }

    private void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
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
            hint.Foreground = palette.MutedTextBrush;
        }
    }

    private static int ReadInt(NumericUpDown input, int fallback)
    {
        if (input.Value is null)
        {
            return fallback;
        }

        return decimal.ToInt32(decimal.Round(input.Value.Value, 0, MidpointRounding.AwayFromZero));
    }
}
