using Avalonia.Threading;
using AgentDeck.ViewModels;

namespace AgentDeck.Terminal;

/// <summary>
/// Периодически сверяет рабочую директорию процессов тайлов с той, что показана
/// в заголовке: «cd» внутри терминала должен доезжать до шапки тайла.
/// </summary>
public sealed class WorkingDirectoryPoller : IDisposable
{
    /// <summary>
    /// Период опроса.
    /// </summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    private readonly DeckViewModel _deck;
    private readonly Func<TileViewModel, string?> _probe;
    private readonly Dictionary<Guid, Tracked> _tracked = [];
    private readonly DispatcherTimer? _timer;

    /// <summary>
    /// Создаёт опрашиватель для указанного дека.
    /// </summary>
    /// <param name="deck">
    /// Дек, тайлы которого опрашиваются.
    /// </param>
    /// <param name="interval">
    /// Период опроса; по умолчанию 500 мс.
    /// </param>
    /// <param name="probe">
    /// Чтение рабочей директории тайла; по умолчанию системное — через «/proc».
    /// </param>
    /// <param name="useTimer">
    /// false отключает собственный таймер — тик подаётся вручную из тестов.
    /// </param>
    public WorkingDirectoryPoller(
        DeckViewModel deck,
        TimeSpan? interval = null,
        Func<TileViewModel, string?>? probe = null,
        bool useTimer = true)
    {
        _deck = deck;
        _probe = probe ?? ReadWorkingDirectory;

        if (!useTimer)
        {
            return;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = interval ?? DefaultInterval,
        };

        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>
    /// Директория тайла догнала процесс — сессию стоит переписать.
    /// </summary>
    public event EventHandler? DirectoryChanged;

    /// <summary>
    /// Запускает опрос.
    /// </summary>
    public void Start() => _timer?.Start();

    /// <summary>
    /// Останавливает опрос.
    /// </summary>
    public void Stop() => _timer?.Stop();

    /// <summary>
    /// Выполняет один цикл опроса всех тайлов дека.
    /// </summary>
    public void Tick()
    {
        var alive = new HashSet<Guid>();

        foreach (var tile in _deck.Tiles)
        {
            // Плейсхолдеру путь правит пользователь — подменять его нельзя, да и
            // процесса, у которого можно спросить cwd, у тайла ещё нет.
            if (tile.IsPlaceholder || _probe(tile) is not { Length: > 0 } directory)
            {
                continue;
            }

            alive.Add(tile.Id);
            Apply(tile, directory);
        }

        // Тайл закрыли или его процесс умер: сохранённая точка отсчёта относится
        // к прошлой жизни тайла, и перезапуск должен начать отсчёт заново.
        foreach (var id in _tracked.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _tracked.Remove(id);
        }
    }

    /// <summary>
    /// Забывает всё, что известно о директории тайла — вызывается при
    /// перезапуске процесса.
    /// </summary>
    public void Reset(Guid tileId) => _tracked.Remove(tileId);

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _tracked.Clear();
    }

    /// <summary>
    /// Рабочая директория процесса, который держит терминал тайла.
    /// </summary>
    private static string? ReadWorkingDirectory(TileViewModel tile)
        => tile.Terminal?.Pid is { } pid ? ProcessDirectory.ReadForeground(pid) : null;

    /// <summary>
    /// Сверяет прочитанный путь с известным и, если процесс действительно
    /// сменил директорию, переносит её в тайл.
    /// </summary>
    private void Apply(TileViewModel tile, string directory)
    {
        if (!_tracked.TryGetValue(tile.Id, out var tracked))
        {
            // Первый ответ процесса — точка отсчёта, а не смена пути: ядро
            // отдаёт путь без символических ссылок, и с тем, которым тайл
            // запускали, он совпадает не всегда. Переписывать из-за этого
            // заголовок нечестно — «cd» ещё не было.
            _tracked[tile.Id] = new Tracked(directory);
            return;
        }

        if (string.Equals(tracked.Applied, directory, StringComparison.Ordinal))
        {
            tracked.Pending = null;
            return;
        }

        // Новый путь принимаем со второго тика подряд: команда переднего плана
        // могла уйти в свой каталог на доли секунды («(cd build && make)»,
        // git-хук, установщик), и заголовок мигал бы туда и обратно.
        if (!string.Equals(tracked.Pending, directory, StringComparison.Ordinal))
        {
            tracked.Pending = directory;
            return;
        }

        tracked.Applied = directory;
        tracked.Pending = null;

        tile.SyncWorkingDirectory(directory);
        DirectoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Что известно про директорию тайла.
    /// </summary>
    /// <param name="applied">
    /// Путь, который тайл уже показывает.
    /// </param>
    private sealed class Tracked(string applied)
    {
        /// <summary>
        /// Последний путь, принятый тайлом.
        /// </summary>
        public string Applied { get; set; } = applied;

        /// <summary>
        /// Путь, увиденный на прошлом тике и ждущий подтверждения; null — ждать
        /// нечего.
        /// </summary>
        public string? Pending { get; set; }
    }
}
