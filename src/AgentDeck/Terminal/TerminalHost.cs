using Avalonia.Threading;
using SvcSystems.UI.Terminal;

namespace AgentDeck.Terminal;

/// <summary>
/// Клей между PTY-процессом и экранной моделью терминала: вывод PTY уходит в
/// VT-парсер, пользовательский ввод и изменения размера — обратно в PTY.
/// Даёт снимок буфера и счётчик изменений для детектора статусов.
/// </summary>
public sealed class TerminalHost : IAsyncDisposable
{
    private readonly TerminalControlModel _model;
    private readonly OutputBuffer _output = new();
    private readonly Lock _gate = new();

    // Не освобождается намеренно: старт PTY может держать связанный токен в
    // момент гашения, а dispose источника обрушил бы его ObjectDisposedException.
    private readonly CancellationTokenSource _lifetime = new();

    private PtySession? _session;
    private bool _disposed;
    private int _exitRaised;
    private long _changeCounter;
    private int _cols = 80;
    private int _rows = 24;

    /// <summary>
    /// Создаёт хост с собственной экранной моделью терминала.
    /// </summary>
    public TerminalHost()
    {
        _model = new TerminalControlModel(new TerminalOptions
        {
            Cols = _cols,
            Rows = _rows,
            Scrollback = 5000,
            TermName = AgentLaunchProfile.TerminalType,

            // Ведомый конец PTY отдаётся в raw-режиме (без ONLCR), поэтому
            // перевод строки приходит голым \n — возврат каретки делаем сами,
            // иначе построчный вывод «лесенкой» уползает вправо.
            ConvertEol = true,
        });

        _model.UserInput += OnUserInput;
        _model.SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// Процесс тайла завершился; аргумент — код возврата.
    /// </summary>
    public event EventHandler<int>? Exited;

    /// <summary>
    /// Экранная модель для <c>TerminalControl</c>.
    /// </summary>
    public TerminalControlModel Model => _model;

    /// <summary>
    /// Профиль, которым запущен (или был запущен) процесс.
    /// </summary>
    public AgentLaunchProfile? Profile { get; private set; }

    /// <summary>
    /// Процесс запущен и ещё жив.
    /// </summary>
    public bool IsRunning => _session?.IsRunning == true;

    /// <summary>
    /// Код возврата завершившегося процесса.
    /// </summary>
    public int? ExitCode => _session?.ExitCode;

    /// <summary>
    /// Монотонный счётчик изменений буфера: рост означает активность процесса.
    /// </summary>
    public long ChangeCounter => Interlocked.Read(ref _changeCounter);

    /// <summary>
    /// Запускает процесс по профилю. Повторный запуск сначала гасит предыдущий.
    /// </summary>
    public async Task StartAsync(AgentLaunchProfile profile, CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);

        Profile = profile;
        Interlocked.Exchange(ref _exitRaised, 0);

        if (await SpawnAsync(profile, cancellationToken).ConfigureAwait(false) is not { } session)
        {
            return;
        }

        session.Exited += OnSessionExited;

        var attached = false;

        lock (_gate)
        {
            if (!_disposed)
            {
                _session = session;
                attached = true;
            }
        }

        if (!attached)
        {
            // Хост погасили, пока PTY поднимался: процесс уже не попадёт ни в
            // StopAsync, ни в общий ShutdownAsync — гасим его прямо здесь,
            // иначе он останется сиротой.
            session.Exited -= OnSessionExited;
            await session.DisposeAsync().ConfigureAwait(false);
            return;
        }

        // Совсем короткая команда могла завершиться до подписки на Exited:
        // события уже не будет, а тайл ждёт его, чтобы закрыться.
        if (!session.IsRunning)
        {
            OnSessionExited(session, session.ExitCode ?? 0);
        }
    }

    /// <summary>
    /// Поднимает PTY, отменяя старт вместе с гашением хоста.
    /// </summary>
    /// <returns>null, если старт отменён гашением.</returns>
    private async Task<PtySession?> SpawnAsync(AgentLaunchProfile profile, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

        try
        {
            return await PtySession.StartAsync(
                profile,
                _cols,
                _rows,
                OnOutput,
                null,
                linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Отмена пришла от гашения хоста, а не от вызывающего — это не ошибка запуска.
            return null;
        }
    }

    /// <summary>
    /// Возвращает последние строки видимой области буфера — вход детектора статусов.
    /// </summary>
    public IReadOnlyList<string> SnapshotLastRows(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        try
        {
            var lines = _model.Terminal?.Engine?.GetVisibleLines() ?? [];
            return lines.Length <= count ? lines : lines[^count..];
        }
        catch (Exception exception) when (exception is InvalidOperationException or NullReferenceException)
        {
            // Буфер перестраивается параллельно с ресайзом — снимок пропускаем.
            return [];
        }
    }

    /// <summary>
    /// Полностью очищает экран и scrollback перед перезапуском в том же тайле.
    /// </summary>
    public void Reset()
    {
        _output.Clear();
        _model.Terminal?.Engine?.Reset();
        _model.FullBufferUpdate();
        Interlocked.Exchange(ref _changeCounter, 0);
    }

    /// <summary>
    /// Гасит процесс и освобождает PTY.
    /// </summary>
    public async Task StopAsync()
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        session.Exited -= OnSessionExited;
        await session.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);

        _model.UserInput -= OnUserInput;
        _model.SizeChanged -= OnSizeChanged;
        await StopAsync().ConfigureAwait(false);
    }

    private void OnOutput(byte[] data)
    {
        // Разбор VT и обновление UI обязаны идти в UI-потоке, но задача на чанк
        // означала бы неограниченную очередь диспетчера: болтливый процесс
        // отдаёт десятки тысяч чанков в секунду. Пачка схлопывается в одну.
        if (_output.Append(data))
        {
            Dispatcher.UIThread.Post(DrainOutput, DispatcherPriority.Background);
        }
    }

    private void DrainOutput()
    {
        var batch = _output.Drain();

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var chunk in batch)
        {
            _model.Feed(chunk, chunk.Length);
        }

        Interlocked.Increment(ref _changeCounter);
    }

    private void OnUserInput(object? sender, TerminalUserInputEventArgs e) => _session?.Write(e.Data.Span);

    private void OnSizeChanged(object? sender, TerminalSizeChangedEventArgs e)
    {
        if (e.Cols <= 0 || e.Rows <= 0)
        {
            return;
        }

        _cols = e.Cols;
        _rows = e.Rows;
        _session?.Resize(e.Cols, e.Rows);
    }

    private void OnSessionExited(object? sender, int exitCode)
    {
        // Событие и досылка после подписки могут прийти обе — тайл должен
        // узнать о завершении ровно один раз.
        if (Interlocked.Exchange(ref _exitRaised, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => Exited?.Invoke(this, exitCode));
    }
}
