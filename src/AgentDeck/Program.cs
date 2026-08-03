using Avalonia;

namespace AgentDeck;

/// <summary>
/// Точка входа приложения.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Идентификатор приложения в рабочем окружении: имя .desktop-файла и имя
    /// иконки в теме hicolor. Оболочка сопоставляет окно с ярлыком по WM_CLASS,
    /// поэтому без этого значения панель задач берёт иконку не из темы.
    /// </summary>
    private const string AppId = "agentdeck";

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
            .With(new X11PlatformOptions { WmClass = AppId })
            .LogToTrace();
}
