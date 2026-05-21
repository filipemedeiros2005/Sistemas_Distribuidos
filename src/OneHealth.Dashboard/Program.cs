using Avalonia;
using System;

namespace OneHealth.Dashboard;

/// <summary>
/// Process entry point. Boots Avalonia in classic desktop mode — meaning a
/// single foreground window owned by <see cref="App"/>. The Avalonia visual
/// designer also calls <see cref="BuildAvaloniaApp"/> reflectively, so the
/// signature must stay stable.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Initialization code. Don't use any Avalonia, third-party APIs or any
    /// <c>SynchronizationContext</c>-reliant code before <c>AppMain</c> is
    /// called: things aren't initialized yet and stuff might break.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Builds the <see cref="AppBuilder"/> graph used by both
    /// <see cref="Main"/> and the Avalonia visual designer. Don't remove or
    /// rename; the designer looks up this method by convention.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
