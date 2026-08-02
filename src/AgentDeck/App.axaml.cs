using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AgentDeck;

/// <summary>
/// Корневой класс приложения.
/// </summary>
public class App : Application
{
    /// <summary>
    /// Сколько ждём гашения PTY при выходе. Процессы уже получили kill, и
    /// застывшее окно вместо закрытия хуже, чем недождавшийся дескриптор.
    /// </summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private MainWindow? _mainWindow;

    /// <summary>
    /// Загружает XAML-разметку приложения.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Создаёт главное окно после завершения инициализации фреймворка.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Гасит все PTY-процессы перед выходом, чтобы не оставить сирот.
    /// </summary>
    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_mainWindow is not { } window)
        {
            return;
        }

        try
        {
            window.Deck.ShutdownAsync().Wait(ShutdownTimeout);
        }
        catch (Exception exception) when (exception is AggregateException or OperationCanceledException)
        {
            // Сорвавшееся гашение оставит максимум осиротевший процесс,
            // а необработанное исключение здесь превратило бы выход в падение.
        }
    }
}
