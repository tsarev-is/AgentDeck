using AgentDeck.Models;

namespace AgentDeck.Status;

/// <summary>
/// Снимок состояния тайла на очередном тике опроса.
/// </summary>
/// <param name="Rows">
/// Последние строки видимой области буфера.
/// </param>
/// <param name="ChangeCounter">
/// Монотонный счётчик изменений буфера.
/// </param>
/// <param name="Exited">
/// Процесс завершился.
/// </param>
/// <param name="ExitCode">
/// Код возврата, если процесс завершился.
/// </param>
public readonly record struct StatusSnapshot(
    IReadOnlyList<string> Rows,
    long ChangeCounter,
    bool Exited,
    int? ExitCode);

/// <summary>
/// Детектор статуса тайла — чистая функция снимка и времени.
/// Слои по убыванию приоритета: код возврата, устоявшийся паттерн вывода,
/// активность буфера.
/// </summary>
public sealed class AgentStatusDetector
{
    /// <summary>
    /// Сколько паттерн должен стабильно присутствовать или отсутствовать,
    /// прежде чем поменяет статус. Защита от ложных срабатываний на перерисовке.
    /// </summary>
    public static readonly TimeSpan DefaultPersistence = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// Сколько буфер должен простоять без изменений, чтобы считать, что агент ждёт ввода.
    /// </summary>
    public static readonly TimeSpan DefaultIdleAfter = TimeSpan.FromSeconds(2);

    private readonly AgentKind _kind;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _persistence;
    private readonly TimeSpan _idleAfter;

    private AgentSignal? _observedSignal;
    private DateTimeOffset _observedSince;
    private AgentSignal? _confirmedSignal;

    private long _lastCounter;
    private DateTimeOffset _lastChangeAt;
    private bool _started;

    /// <summary>
    /// Создаёт детектор для указанного CLI.
    /// </summary>
    public AgentStatusDetector(
        AgentKind kind,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? persistence = null,
        TimeSpan? idleAfter = null)
    {
        _kind = kind;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _persistence = persistence ?? DefaultPersistence;
        _idleAfter = idleAfter ?? DefaultIdleAfter;
        Status = TileStatus.Running;
    }

    /// <summary>
    /// Текущий статус тайла.
    /// </summary>
    public TileStatus Status { get; private set; }

    /// <summary>
    /// Обрабатывает очередной снимок и возвращает актуальный статус.
    /// </summary>
    public TileStatus Update(StatusSnapshot snapshot)
    {
        var now = _clock();

        if (!_started)
        {
            _started = true;
            _lastCounter = snapshot.ChangeCounter;
            _lastChangeAt = now;
            _observedSince = now;
        }

        // Слой 1: код возврата перекрывает всё остальное.
        if (snapshot.Exited)
        {
            Status = snapshot.ExitCode is null or 0 ? TileStatus.Finished : TileStatus.Crashed;
            return Status;
        }

        // Слой 2: паттерны вывода с debounce устойчивости.
        var signal = AgentPatterns.Match(_kind, snapshot.Rows);

        if (signal != _observedSignal)
        {
            _observedSignal = signal;
            _observedSince = now;
        }
        else if (now - _observedSince >= _persistence)
        {
            _confirmedSignal = signal;
        }

        // Слой 3: активность буфера.
        if (snapshot.ChangeCounter != _lastCounter)
        {
            _lastCounter = snapshot.ChangeCounter;
            _lastChangeAt = now;
        }

        Status = _confirmedSignal switch
        {
            AgentSignal.Permission => TileStatus.AwaitingPermission,

            // Маркер занятости удерживает Running даже при замершем буфере.
            AgentSignal.Busy => TileStatus.Running,

            _ => now - _lastChangeAt >= _idleAfter ? TileStatus.AwaitingInput : TileStatus.Running,
        };

        return Status;
    }
}
