using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AgentDeck.Session;
using AgentDeck.Settings;
using AgentDeck.Status;
using AgentDeck.ViewModels;
using AgentDeck.Views;

namespace AgentDeck;

/// <summary>
/// Главное окно приложения: titlebar и дек тайлов.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Задержка сохранения сессии после структурного изменения раскладки.
    /// </summary>
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    private readonly StatusPoller _statusPoller;
    private readonly SessionStore _sessionStore;
    private readonly SettingsStore _settingsStore;
    private readonly DispatcherTimer _saveTimer;

    private AppSettings _settings;
    private bool _sessionSaved;
    private bool _settingsOpen;

    /// <summary>
    /// Создаёт главное окно с хранилищами в каталоге пользователя.
    /// </summary>
    public MainWindow()
        : this(new SessionStore(), new SettingsStore())
    {
    }

    /// <summary>
    /// Создаёт главное окно поверх указанных хранилищ сессии и настроек.
    /// </summary>
    public MainWindow(SessionStore sessionStore, SettingsStore settingsStore)
    {
        _sessionStore = sessionStore;
        _settingsStore = settingsStore;

        // Настройки читаются до дека: тайлы должны сразу получить свои кнопки запуска.
        _settings = settingsStore.Load();

        var deck = new DeckViewModel(_settings);
        Deck = deck;
        DataContext = deck;
        InitializeComponent();

        // Восстановление до первого показа: раскладка и геометрия окна должны
        // быть готовы к первому кадру.
        var state = sessionStore.Load();
        if (deck.RestoreSession(state))
        {
            ApplyWindowState(state!.Window);
        }

        _statusPoller = new StatusPoller(deck);

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = SaveDebounce };
        _saveTimer.Tick += OnSaveTimerTick;

        deck.LayoutChanged += OnDeckLayoutChanged;
        deck.RestartRequested += (_, tile) => _statusPoller.Reset(tile.Id);
        deck.TileClosed += (_, tile) => _statusPoller.Reset(tile.Id);
        deck.SettingsRequested += (_, _) => _ = OpenSettingsAsync();

        Opened += (_, _) => _statusPoller.Start();
        Closing += (_, _) => SaveSession();
        Closed += (_, _) => _statusPoller.Dispose();
    }

    /// <summary>
    /// Модель дека этого окна.
    /// </summary>
    public DeckViewModel Deck { get; }

    /// <summary>
    /// Опрашиватель статусов тайлов.
    /// </summary>
    public StatusPoller StatusPoller => _statusPoller;

    /// <summary>
    /// Немедленно сохраняет текущую сессию.
    /// </summary>
    public void SaveSession()
    {
        _saveTimer.Stop();

        var state = Deck.CaptureSession();
        state.Window = new Session.WindowState
        {
            X = Position.X,
            Y = Position.Y,
            Width = Width,
            Height = Height,
            Maximized = WindowState == Avalonia.Controls.WindowState.Maximized,
        };

        _sessionSaved = _sessionStore.Save(state);
    }

    /// <summary>
    /// Признак успешного сохранения последней сессии — используется в проверках.
    /// </summary>
    public bool SessionSaved => _sessionSaved;

    /// <summary>
    /// Текущие применённые настройки.
    /// </summary>
    public AppSettings Settings => _settings;

    /// <summary>
    /// Открывает окно настроек и применяет результат: сохраняет на диск и
    /// пересобирает кнопки запуска во всех тайлах.
    /// </summary>
    public async Task OpenSettingsAsync()
    {
        // Запрос настроек может прийти сразу от нескольких тайлов.
        if (_settingsOpen)
        {
            return;
        }

        _settingsOpen = true;

        try
        {
            var window = new SettingsWindow(new SettingsViewModel(_settings));

            if (await window.ShowDialog<AppSettings?>(this) is not { } saved)
            {
                return;
            }

            _settings = saved;
            _settingsStore.Save(saved);
            Deck.ApplyUtilities(saved);
        }
        finally
        {
            _settingsOpen = false;
        }
    }

    private void OnAddTileClick(object? sender, RoutedEventArgs e) => Deck.AddTile();

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => _ = OpenSettingsAsync();

    private void OnDeckLayoutChanged(object? sender, EventArgs e)
    {
        // Перезапуск таймера схлопывает пачку изменений в одну запись на диск.
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTimerTick(object? sender, EventArgs e) => SaveSession();

    /// <summary>
    /// Применяет сохранённые размер и положение окна, оставаясь в пределах экрана.
    /// </summary>
    private void ApplyWindowState(Session.WindowState? window)
    {
        if (window is null)
        {
            return;
        }

        if (window.Width is > 0 and { } width)
        {
            Width = Math.Max(MinWidth, width);
        }

        if (window.Height is > 0 and { } height)
        {
            Height = Math.Max(MinHeight, height);
        }

        if (window.X is { } x && window.Y is { } y && IsOnConnectedScreen(new PixelPoint(x, y)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (window.Maximized)
        {
            WindowState = Avalonia.Controls.WindowState.Maximized;
        }
    }

    /// <summary>
    /// Точка лежит на одном из подключённых сейчас экранов. Монитор, на котором
    /// сохранялась сессия, могли отключить — тогда прежние координаты унесли бы
    /// окно за пределы рабочего стола, и вернуть его оттуда было бы нечем.
    /// </summary>
    private bool IsOnConnectedScreen(PixelPoint point)
    {
        try
        {
            var screens = Screens?.All;

            // Без сведений об экранах не рискуем — пусть окно встанет по центру.
            return screens is { Count: > 0 } && screens.Any(screen => screen.WorkingArea.Contains(point));
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            // Бэкенд ещё не готов отвечать про экраны — центрирование безопаснее.
            return false;
        }
    }
}
