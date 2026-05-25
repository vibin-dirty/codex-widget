using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using CodexWidget.Core;

namespace CodexWidget.App;

internal sealed class App : Application
{
    private AppCompositionRoot? _compositionRoot;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _compositionRoot = new AppCompositionRoot(desktop);
            _compositionRoot.Initialize();
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit -= OnDesktopExit;
        }

        _compositionRoot?.Dispose();
        _compositionRoot = null;
    }

    internal void ApplyThemePreference(WidgetThemePreference theme)
    {
        RequestedThemeVariant = theme switch
        {
            WidgetThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Light,
        };
    }
}
