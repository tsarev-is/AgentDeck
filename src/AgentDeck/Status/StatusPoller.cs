using Avalonia.Threading;
using AgentDeck.Models;
using AgentDeck.ViewModels;

namespace AgentDeck.Status;

/// <summary>
/// Периодически снимает состояние запущенных тайлов и прогоняет его через
/// детектор, обновляя статус во ViewModel.
/// </summary>
public sealed class StatusPoller : IDisposable
{
    /// <summary>
    /// Период опроса.
    /// </summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Сколько последних строк буфера отдаётся детектору.
    /// </summary>
    public const int SnapshotRows = 30;

    private readonly DeckViewModel _deck;
    private readonly Func<DateTimeOffset>? _clock;
    private readonly Dictionary<Guid, (AgentKind Kind, AgentStatusDetector Detector)> _detectors = [];
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
    /// <param name="clock">
    /// Часы детекторов; по умолчанию системные.
    /// </param>
    /// <param name="useTimer">
    /// false отключает собственный таймер — тик подаётся вручную из тестов.
    /// </param>
    public StatusPoller(
        DeckViewModel deck,
        TimeSpan? interval = null,
        Func<DateTimeOffset>? clock = null,
        bool useTimer = true)
    {
        _deck = deck;
        _clock = clock;

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
            // Плейсхолдер не опрашивается: детектор должен начаться заново после запуска.
            if (tile.Terminal is not { } host || tile.Status == TileStatus.Placeholder || tile.AgentKind is not { } kind)
            {
                continue;
            }

            alive.Add(tile.Id);

            if (!_detectors.TryGetValue(tile.Id, out var entry) || entry.Kind != kind)
            {
                entry = (kind, new AgentStatusDetector(kind, _clock));
                _detectors[tile.Id] = entry;
            }

            tile.Status = entry.Detector.Update(new StatusSnapshot(
                host.SnapshotLastRows(SnapshotRows),
                host.ChangeCounter,
                !host.IsRunning && host.ExitCode is not null,
                host.ExitCode));
        }

        foreach (var id in _detectors.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _detectors.Remove(id);
        }
    }

    /// <summary>
    /// Сбрасывает детектор тайла — вызывается при перезапуске процесса.
    /// </summary>
    public void Reset(Guid tileId) => _detectors.Remove(tileId);

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _detectors.Clear();
    }
}
