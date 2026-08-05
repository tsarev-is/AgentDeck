using AgentDeck.Models;

namespace AgentDeck.Status;

/// <summary>
/// Снимок состояния тайла на очередном тике опроса.
/// </summary>
/// <param name="Rows">
/// Последние строки видимой области буфера.
/// </param>
/// <param name="Exited">
/// Процесс завершился.
/// </param>
/// <param name="ExitCode">
/// Код возврата, если процесс завершился.
/// </param>
public readonly record struct StatusSnapshot(
    IReadOnlyList<string> Rows,
    bool Exited,
    int? ExitCode);

/// <summary>
/// Детектор статуса тайла — чистая функция снимка и времени.
/// Слои по убыванию приоритета: код возврата, тип утилиты, маркеры на экране.
/// </summary>
/// <remarks>
/// Занятость определяется только маркером, который агент печатает сам
/// («esc to interrupt», «✻ Scurrying… (4s)»), а не активностью буфера: буфер
/// меняется и от набора текста в поле ввода, и от перерисовки интерфейса, так
/// что по нему «модель работает» от «пользователь печатает» не отличить.
/// </remarks>
public sealed class AgentStatusDetector
{
    /// <summary>
    /// Сколько сигнал должен стабильно держаться на экране, прежде чем поменяет
    /// статус. Защита от ложных срабатываний на перерисовке.
    /// </summary>
    public static readonly TimeSpan DefaultPersistence = TimeSpan.FromMilliseconds(750);

    private readonly AgentKind _kind;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _persistence;

    private AgentSignal? _observedSignal;
    private DateTimeOffset _observedSince;
    private AgentSignal? _confirmedSignal;
    private bool _started;

    /// <summary>
    /// Создаёт детектор для указанного CLI.
    /// </summary>
    /// <param name="kind">
    /// Тип запущенной утилиты — от него зависит набор паттернов.
    /// </param>
    /// <param name="clock">
    /// Часы детектора; по умолчанию системные.
    /// </param>
    /// <param name="persistence">
    /// Выдержка сигнала; по умолчанию <see cref="DefaultPersistence"/>.
    /// </param>
    public AgentStatusDetector(
        AgentKind kind,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? persistence = null)
    {
        _kind = kind;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _persistence = persistence ?? DefaultPersistence;
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
            _observedSince = now;
        }

        // Слой 1: код возврата перекрывает всё остальное.
        if (snapshot.Exited)
        {
            Status = snapshot.ExitCode is null or 0 ? TileStatus.Finished : TileStatus.Crashed;
            return Status;
        }

        // Слой 2: у обычного терминала состояний нет. Ни занятость, ни ожидание
        // ввода в нём с экрана не читаются: shell ждёт ввода и когда молчит, и
        // когда гоняет сборку с болтливым выводом. Такой тайл просто жив.
        if (!AgentPatterns.HasSignals(_kind))
        {
            Status = TileStatus.Running;
            return Status;
        }

        // Пустой экран — ещё не отсутствие маркера. Первый опрос приходит
        // раньше, чем CLI успевает нарисовать хоть что-то, и приняв эту пустоту
        // за отработанный запрос, тайл объявил бы «ход за пользователем» прямо
        // на запуске: сплошной акцент в рамке и точке. Пока рисовать нечего,
        // статус остаётся прежним.
        if (snapshot.Rows.Count == 0)
        {
            return Status;
        }

        // Слой 3: маркеры на экране с выдержкой устойчивости.
        var signal = AgentPatterns.Match(_kind, snapshot.Rows);

        if (signal != _observedSignal)
        {
            _observedSignal = signal;
            _observedSince = now;
        }

        // Маркер занятости агент печатает сам, спутать его не с чем — занятость
        // подтверждается сразу, иначе короткий запрос успел бы отработать, ни
        // разу не моргнув лампочкой. Уход сигнала, наоборот, требует выдержки:
        // перерисовка экрана роняет строку с маркером на отдельный кадр.
        if (signal is AgentSignal.Busy || now - _observedSince >= _persistence)
        {
            _confirmedSignal = signal;
        }

        Status = _confirmedSignal switch
        {
            AgentSignal.Permission => TileStatus.AwaitingPermission,
            AgentSignal.Busy => TileStatus.Working,

            // Маркера работы на экране нет — запрос отработан, ход за пользователем.
            _ => TileStatus.AwaitingInput,
        };

        return Status;
    }
}
