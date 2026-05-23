using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexWidget.Core;

namespace CodexWidget.App;

internal sealed class SettingsWindow : Window
{
    private readonly WidgetPreferenceCoordinator _preferenceCoordinator;
    private readonly ComboBox _selectedViewComboBox;
    private readonly NumericUpDown _widgetScaleInput;
    private readonly CheckBox _alwaysOnTopCheckBox;
    private readonly NumericUpDown _refreshPeriodInput;
    private readonly TextBlock _statusTextBlock;

    public SettingsWindow(WindowIcon? icon, WidgetPreferenceCoordinator preferenceCoordinator)
    {
        _preferenceCoordinator = preferenceCoordinator ?? throw new ArgumentNullException(nameof(preferenceCoordinator));

        Title = "Settings";
        Icon = icon;
        Width = 420;
        Height = 370;
        MinWidth = 420;
        MinHeight = 370;
        MaxWidth = 420;
        MaxHeight = 370;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _selectedViewComboBox = new ComboBox
        {
            ItemsSource = new[] { WidgetViewKind.Minimal, WidgetViewKind.Compact, WidgetViewKind.Full },
            SelectedIndex = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
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
            Foreground = Brushes.Gray,
            MaxWidth = 380,
        };

        Content = new Border
        {
            Padding = new Thickness(16),
            Child = BuildContent()
        };

        ReloadFromPreferences();
    }

    public void ReloadFromPreferences()
    {
        ApplyDraftToControls(_preferenceCoordinator.CreateDraft());
        _statusTextBlock.Text = string.Empty;
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

    private static Control CreateFieldRow(string label, Control control)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 190,
        });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static TextBlock CreateHintText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
        };
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
        };
    }

    private void ApplyDraftToControls(WidgetPreferenceDraft draft)
    {
        _selectedViewComboBox.SelectedItem = draft.SelectedView;
        _widgetScaleInput.Value = draft.WidgetScalePercent;
        _alwaysOnTopCheckBox.IsChecked = draft.AlwaysOnTop;
        _refreshPeriodInput.Value = draft.RefreshPeriodSeconds;
    }

    private void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var outcome = _preferenceCoordinator.SaveAndApply(BuildDraftFromControls());
        if (outcome.AppliedPreferences is not null)
        {
            _preferenceCoordinator.UpdateCurrentPreferences(outcome.AppliedPreferences);
            ApplyDraftToControls(_preferenceCoordinator.CreateDraft());
        }

        _statusTextBlock.Foreground = outcome.Succeeded ? Brushes.DarkGreen : Brushes.IndianRed;
        _statusTextBlock.Text = outcome.Messages.Count == 0
            ? (outcome.Succeeded ? "Preferences saved." : "Preferences could not be saved.")
            : string.Join(" ", outcome.Messages);
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
