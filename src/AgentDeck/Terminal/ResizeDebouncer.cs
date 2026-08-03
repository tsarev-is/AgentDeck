namespace AgentDeck.Terminal;

/// <summary>
/// Trailing-debounce размеров терминала: пачка ресайзов схлопывается в одну
/// отправку последнего размера. Часы инжектируются, тик подаётся извне —
/// логика полностью детерминирована и тестируема.
/// Запросы приходят из UI-потока, тики — с таймера, поэтому состояние живёт
/// под замком; сама отправка в PTY идёт снаружи замка.
/// </summary>
public sealed class ResizeDebouncer
{
    /// <summary>
    /// Задержка по умолчанию — компромисс между отзывчивостью и гонками ConPTY.
    /// </summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(100);

    private readonly Lock _gate = new();
    private readonly TimeSpan _delay;
    private readonly Action<int, int> _apply;
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _confirmations;

    private (int Cols, int Rows)? _pending;
    private DateTimeOffset _requestedAt;
    private (int Cols, int Rows)? _lastApplied;
    private int _confirmationsLeft;
    private DateTimeOffset _confirmAt;

    /// <summary>
    /// Создаёт debouncer.
    /// </summary>
    /// <param name="apply">
    /// Отправка размера в PTY.
    /// </param>
    /// <param name="delay">
    /// Окно схлопывания; по умолчанию 100 мс.
    /// </param>
    /// <param name="clock">
    /// Источник времени; по умолчанию системные часы.
    /// </param>
    /// <param name="confirmations">
    /// Сколько раз повторить последний размер после стабилизации. На Windows ConPTY
    /// может потерять resize вблизи старта клиента, поэтому там нужен один повтор.
    /// </param>
    public ResizeDebouncer(
        Action<int, int> apply,
        TimeSpan? delay = null,
        Func<DateTimeOffset>? clock = null,
        int confirmations = 0)
    {
        _apply = apply;
        _delay = delay ?? DefaultDelay;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _confirmations = Math.Max(0, confirmations);
    }

    /// <summary>
    /// Размер, отправленный в PTY последним.
    /// </summary>
    public (int Cols, int Rows)? LastApplied
    {
        get
        {
            lock (_gate)
            {
                return _lastApplied;
            }
        }
    }

    /// <summary>
    /// Есть неотправленный запрос или неотданное подтверждение.
    /// </summary>
    public bool HasWork
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null || _confirmationsLeft > 0;
            }
        }
    }

    /// <summary>
    /// Запрашивает новый размер; отправка произойдёт не раньше, чем через задержку.
    /// </summary>
    public void Request(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _pending = (cols, rows);
            _requestedAt = _clock();
        }
    }

    /// <summary>
    /// Продвигает состояние по времени. Возвращает true, если размер был отправлен.
    /// </summary>
    public bool Tick()
    {
        (int Cols, int Rows)? send;
        bool consumed;

        lock (_gate)
        {
            var now = _clock();

            if (_pending is { } pending && now - _requestedAt >= _delay)
            {
                _pending = null;
                send = Take(pending, now);
                consumed = true;
            }
            else if (_confirmationsLeft > 0 && _lastApplied is { } last && now >= _confirmAt)
            {
                _confirmationsLeft--;
                _confirmAt = now + _delay;
                send = last;
                consumed = true;
            }
            else
            {
                send = null;
                consumed = false;
            }
        }

        Send(send);
        return consumed;
    }

    /// <summary>
    /// Немедленно отправляет отложенный размер, минуя задержку.
    /// </summary>
    public bool Flush()
    {
        (int Cols, int Rows)? send;

        lock (_gate)
        {
            if (_pending is not { } pending)
            {
                return false;
            }

            _pending = null;
            send = Take(pending, _clock());
        }

        Send(send);
        return true;
    }

    /// <summary>
    /// Отправляет размер в PTY вне замка: ioctl не должен держать UI-поток,
    /// который в это время может запрашивать следующий ресайз. Порядок отправок
    /// держится на том, что тикает один поток (таймер сессии); если
    /// <see cref="Flush"/> начнут звать параллельно с <see cref="Tick"/>,
    /// отправки придётся сериализовать.
    /// </summary>
    private void Send((int Cols, int Rows)? size)
    {
        if (size is { } value)
        {
            _apply(value.Cols, value.Rows);
        }
    }

    /// <summary>
    /// Отмечает размер как отправленный и возвращает его. Вызывается под замком.
    /// </summary>
    /// <returns>
    /// null, если тот же размер уже отправлен и повторять его незачем.
    /// </returns>
    private (int Cols, int Rows)? Take((int Cols, int Rows) size, DateTimeOffset now)
    {
        // Повторная отправка того же размера ничего не меняет — пропускаем.
        if (_lastApplied == size && _confirmationsLeft == 0)
        {
            return null;
        }

        _lastApplied = size;
        _confirmationsLeft = _confirmations;
        _confirmAt = now + _delay;

        return size;
    }
}
