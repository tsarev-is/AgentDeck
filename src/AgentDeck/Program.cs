using Avalonia;

namespace AgentDeck;

/// <summary>
/// Точка входа приложения.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Инициализирует и запускает Avalonia-приложение.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Конфигурирует Avalonia-приложение.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
