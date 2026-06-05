using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace OneHealth.Dashboard;

/// <summary>
/// Avalonia application root. Loaded once at process start by <c>Program.cs</c>,
/// then handed control of the desktop lifetime. Owns nothing beyond the main
/// window — every runtime resource (TCP client, view models) lives inside
/// <see cref="MainWindow"/>.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Parses <c>App.axaml</c> at startup. Called by the Avalonia framework
    /// before <see cref="OnFrameworkInitializationCompleted"/>.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Resolves the active lifetime (always desktop for this project) and
    /// instantiates the <see cref="MainWindow"/>. Always call the base
    /// implementation last so the framework can wire up routing and themes.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
