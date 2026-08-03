using System.Collections.ObjectModel;
using AgentDeck.Layout;
using AgentDeck.Models;
using AgentDeck.Session;
using AgentDeck.Settings;

namespace AgentDeck.ViewModels;

/// <summary>
/// Дек целиком: дерево раскладки и коллекция тайлов, синхронизированные один к одному.
/// </summary>
public sealed class DeckViewModel : ViewModelBase
{
    private readonly ObservableCollection<TileViewModel> _tiles = [];
    private readonly CommandResolver _commandResolver;
    private readonly DirectoryBrowser _browser;

    private IReadOnlyList<UtilityState> _utilities = [];

    /// <summary>
    /// Создаёт пустой дек поверх указанных настроек.
    /// </summary>
    /// <param name="settings">
    /// Настройки приложения; по умолчанию — штатные.
    /// </param>
    /// <param name="commandResolver">
    /// Проверка команд перед запуском; по умолчанию системная.
    /// </param>
    /// <param name="browser">
    /// Чтение вложенных директорий тайлами; по умолчанию системное.
    /// </param>
    public DeckViewModel(
        AppSettings? settings = null,
        CommandResolver? commandResolver = null,
        DirectoryBrowser? browser = null)
    {
        Tiles = new ReadOnlyObservableCollection<TileViewModel>(_tiles);
        _commandResolver = commandResolver ?? new CommandResolver();
        _browser = browser ?? new DirectoryBrowser();
        DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ApplyUtilities(settings ?? AppSettings.CreateDefault());
    }

    /// <summary>
    /// Структура раскладки изменилась — нужно перевыстроить канву и сохранить сессию.
    /// </summary>
    public event EventHandler? LayoutChanged;

    /// <summary>
    /// Тайл просит запустить утилиту.
    /// </summary>
    public event EventHandler<(TileViewModel Tile, LaunchOptionViewModel Option)>? LaunchRequested;

    /// <summary>
    /// Тайл просит открыть настройки.
    /// </summary>
    public event EventHandler<TileViewModel>? SettingsRequested;

    /// <summary>
    /// Тайл просит перезапустить процесс.
    /// </summary>
    public event EventHandler<TileViewModel>? RestartRequested;

    /// <summary>
    /// Тайл закрыт и удалён из дека.
    /// </summary>
    public event EventHandler<TileViewModel>? TileClosed;

    /// <summary>
    /// Дерево раскладки — источник геометрии тайлов.
    /// </summary>
    public LayoutTree Layout { get; private set; } = new();

    /// <summary>
    /// Тайлы дека.
    /// </summary>
    public ReadOnlyObservableCollection<TileViewModel> Tiles { get; }

    /// <summary>
    /// Директория, подставляемая в новый плейсхолдер.
    /// </summary>
    public string DefaultDirectory { get; set; }

    /// <summary>
    /// Кнопка «+ Агент» доступна, пока не достигнут кап в восемь тайлов.
    /// </summary>
    public bool CanAddTile => _tiles.Count < LayoutConstants.MaxTiles;

    /// <summary>
    /// Дек пуст — показывается подсказка по центру.
    /// </summary>
    public bool IsEmpty => _tiles.Count == 0;

    /// <summary>
    /// Утилиты, доступные на плейсхолдерах.
    /// </summary>
    public IReadOnlyList<UtilityState> Utilities => _utilities;

    /// <summary>
    /// Применяет настройки: директория для новых тайлов и набор кнопок запуска
    /// у всех уже существующих тайлов.
    /// </summary>
    public void ApplyUtilities(AppSettings settings)
    {
        // Пустая директория в настройках означает домашнюю папку — иначе
        // очистка поля оставила бы новым тайлам прежнюю до перезапуска.
        DefaultDirectory = string.IsNullOrWhiteSpace(settings.DefaultDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : settings.DefaultDirectory.Trim();

        _utilities = settings.EnabledUtilities();

        foreach (var tile in _tiles)
        {
            tile.SetLaunchOptions(_utilities);
        }
    }

    /// <summary>
    /// Добавляет тайл автоматическим размещением. Возвращает null, если достигнут
    /// кап или в раскладке физически нет места.
    /// </summary>
    public TileViewModel? AddTile() => AddTile(DefaultDirectory, null);

    /// <summary>
    /// Добавляет тайл с указанной директорией и подсказкой сохранённой утилиты.
    /// </summary>
    public TileViewModel? AddTile(string directory, string? suggestedUtility)
    {
        if (!CanAddTile)
        {
            return null;
        }

        var id = Guid.NewGuid();
        if (!Layout.AddTileAuto(id))
        {
            return null;
        }

        var tile = Attach(CreateTile(id, directory));
        tile.SuggestAgent(suggestedUtility);
        _tiles.Add(tile);

        NotifyDeckChanged();
        return tile;
    }

    /// <summary>
    /// Закрывает тайл: удаляет лист раскладки и сам тайл.
    /// </summary>
    public bool CloseTile(Guid tileId)
    {
        var tile = FindTile(tileId);
        if (tile is null)
        {
            return false;
        }

        Layout.Remove(tileId);
        Detach(tile);
        _tiles.Remove(tile);

        // Закрытие тайла обязано убить его процесс — сирот не остаётся.
        _ = ObserveAsync(tile.DisposeAsync().AsTask());

        TileClosed?.Invoke(this, tile);
        NotifyDeckChanged();
        return true;
    }

    /// <summary>
    /// Гасит процессы всех тайлов при закрытии приложения. Тайлы гасятся
    /// параллельно, чтобы выход не растягивался на сумму их таймаутов.
    /// </summary>
    public Task ShutdownAsync()
        => Task.WhenAll(_tiles.ToList().Select(tile => tile.DisposeAsync().AsTask()));

    /// <summary>
    /// Находит тайл по идентификатору.
    /// </summary>
    public TileViewModel? FindTile(Guid tileId) => _tiles.FirstOrDefault(t => t.Id == tileId);

    /// <summary>
    /// Сообщает об изменении геометрии без изменения состава тайлов (ресайз, swap, перенос).
    /// </summary>
    public void NotifyLayoutChanged() => LayoutChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Снимает состояние дека для сохранения сессии. Геометрия окна заполняется вызывающим.
    /// </summary>
    public SessionState CaptureSession() => new()
    {
        LayoutVersion = LayoutSerializer.CurrentVersion,
        Layout = LayoutSerializer.ToDto(Layout).Root,
        Tiles =
        [
            .. _tiles.Select(tile => new TileState
            {
                Id = tile.Id.ToString(),
                Directory = tile.Directory,
                Utility = tile.UtilityName,
            }),
        ],
    };

    /// <summary>
    /// Восстанавливает дек из сохранённой сессии: тайлы возвращаются
    /// плейсхолдерами с префилленной директорией и акцентированной кнопкой
    /// сохранённого агента. Процессы не запускаются.
    /// </summary>
    /// <returns>
    /// false, если состояние повреждено и нужен чистый старт.
    /// </returns>
    public bool RestoreSession(SessionState? state)
    {
        if (state is null)
        {
            return false;
        }

        var layout = LayoutSerializer.FromDto(new LayoutDocumentDto
        {
            LayoutVersion = state.LayoutVersion,
            Root = state.Layout,
        });

        if (layout is null || layout.LeafCount > LayoutConstants.MaxTiles)
        {
            return false;
        }

        var described = new Dictionary<Guid, TileState>();

        foreach (var tile in state.Tiles)
        {
            if (Guid.TryParse(tile.Id, out var id))
            {
                described[id] = tile;
            }
        }

        // Листья без описания тайла и тайлы, отсутствующие в дереве, отбрасываются:
        // источником истины остаётся дерево, приведённое к описанным тайлам.
        foreach (var orphan in layout.TileIds.Where(id => !described.ContainsKey(id)).ToList())
        {
            layout.Remove(orphan);
        }

        var restored = new List<TileViewModel>();

        foreach (var id in layout.TileIds)
        {
            var saved = described[id];
            var tile = CreateTile(id, saved.Directory ?? DefaultDirectory);
            tile.SuggestAgent(saved.Utility ?? LegacyUtilityName(saved.AgentKind));
            restored.Add(tile);
        }

        ReplaceContent(layout, restored);
        return true;
    }

    /// <summary>
    /// Заменяет дерево раскладки и коллекцию тайлов целиком (восстановление сессии).
    /// </summary>
    internal void ReplaceContent(LayoutTree layout, IEnumerable<TileViewModel> tiles)
    {
        foreach (var tile in _tiles)
        {
            Detach(tile);
        }

        _tiles.Clear();
        Layout = layout;

        foreach (var tile in tiles)
        {
            _tiles.Add(Attach(tile));
        }

        NotifyDeckChanged();
    }

    /// <summary>
    /// Создаёт тайл с текущим набором кнопок запуска.
    /// </summary>
    private TileViewModel CreateTile(Guid id, string directory)
    {
        // Единственная точка, где путь попадает в тайл, — значит и единственное
        // место, где его форму стоит нормализовать. Тайл живёт с коротким путём:
        // раскрытый «~» съедал бы место и в поле «cd», и в заголовке.
        var tile = new TileViewModel(id, PathUtilities.CollapseHome(directory), _commandResolver, _browser);
        tile.SetLaunchOptions(_utilities);
        return tile;
    }

    /// <summary>
    /// Имя утилиты из сессий старого формата, где сохранялся элемент перечисления.
    /// </summary>
    private static string? LegacyUtilityName(string? agentKind)
        => Enum.TryParse<AgentKind>(agentKind, out var kind) ? kind.CommandName() : null;

    private TileViewModel Attach(TileViewModel tile)
    {
        tile.CloseRequested += OnTileCloseRequested;
        tile.LaunchRequested += OnTileLaunchRequested;
        tile.RestartRequested += OnTileRestartRequested;
        tile.SettingsRequested += OnTileSettingsRequested;
        return tile;
    }

    private void Detach(TileViewModel tile)
    {
        tile.CloseRequested -= OnTileCloseRequested;
        tile.LaunchRequested -= OnTileLaunchRequested;
        tile.RestartRequested -= OnTileRestartRequested;
        tile.SettingsRequested -= OnTileSettingsRequested;
    }

    private void OnTileCloseRequested(object? sender, EventArgs e)
    {
        if (sender is TileViewModel tile)
        {
            CloseTile(tile.Id);
        }
    }

    private void OnTileLaunchRequested(object? sender, LaunchOptionViewModel option)
    {
        if (sender is not TileViewModel tile)
        {
            return;
        }

        LaunchRequested?.Invoke(this, (tile, option));
        _ = ObserveAsync(tile.LaunchAsync(option));
    }

    private void OnTileSettingsRequested(object? sender, EventArgs e)
    {
        if (sender is TileViewModel tile)
        {
            SettingsRequested?.Invoke(this, tile);
        }
    }

    private void OnTileRestartRequested(object? sender, EventArgs e)
    {
        if (sender is not TileViewModel tile)
        {
            return;
        }

        RestartRequested?.Invoke(this, tile);
        _ = ObserveAsync(tile.RestartAsync());
    }

    /// <summary>
    /// Наблюдает за фоновой операцией тайла. Запуск и гашение живут дольше
    /// обработчика события, и их исключение иначе просто исчезло бы —
    /// о своей ошибке тайл сообщает сам, а ронять UI нельзя.
    /// </summary>
    private static async Task ObserveAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Сорвалось на уже мёртвом PTY — худшее последствие это
            // осиротевший процесс, но не падение приложения.
        }
    }

    private void NotifyDeckChanged()
    {
        OnPropertyChanged(nameof(CanAddTile));
        OnPropertyChanged(nameof(IsEmpty));
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
