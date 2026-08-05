using Avalonia.Threading;
using SvcSystems.UI.Terminal;
using XTerm.Input;

namespace AgentDeck.Terminal;

/// <summary>
/// Клей между PTY-процессом и экранной моделью терминала: вывод PTY уходит в
/// VT-парсер, пользовательский ввод и изменения размера — обратно в PTY.
/// Даёт снимок буфера для детектора статусов.
/// </summary>
public sealed class TerminalHost : IAsyncDisposable
{
    private readonly TerminalControlModel _model;
    private readonly InverseVideo _inverseVideo;
    private readonly MarginScrollback _marginScrollback;
    private readonly OutputBuffer _output = new();
    private readonly Lock _gate = new();

    // Не освобождается намеренно: старт PTY может держать связанный токен в
    // момент гашения, а dispose источника обрушил бы его ObjectDisposedException.
    private readonly CancellationTokenSource _lifetime = new();

    private PtySession? _session;
    private MouseTrackingMode _suspendedMouseTracking;
    private bool _disposed;
    private int _exitRaised;
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

        // Правщик инверсии считает ушедшие вниз строки, поэтому создаётся вместе
        // с моделью — до первой порции вывода.
        _inverseVideo = new InverseVideo(_model);

        // Разбор прокрутки следит за потоком целиком: пропущенное начало
        // оставило бы его с чужими границами региона.
        _marginScrollback = new MarginScrollback(_model);

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
    /// Идентификатор живого процесса тайла; null, пока процесса нет или он уже
    /// завершился — у мёртвого pid спрашивать про процесс нечего, его номер
    /// система вправе выдать кому-то другому.
    /// </summary>
    public int? Pid => _session is { IsRunning: true } session ? session.Pid : null;

    /// <summary>
    /// Код возврата завершившегося процесса.
    /// </summary>
    public int? ExitCode => _session?.ExitCode;

    /// <summary>
    /// В буфере есть выделенный мышью текст.
    /// </summary>
    public bool HasSelection => _model.HasSelection;

    /// <summary>
    /// Выделенный текст; пустая строка, если выделения нет.
    /// </summary>
    public string SelectedText => _model.SelectedText;

    /// <summary>
    /// Процесс включил bracketed paste (DECSET 2004) и ждёт вставку в обёртке.
    /// </summary>
    private bool IsBracketedPaste => _model.Terminal?.Engine?.BracketedPasteMode == true;

    /// <summary>
    /// Режим отслеживания мыши, включённый процессом; <c>None</c> — мышь
    /// принадлежит терминалу.
    /// </summary>
    private MouseTrackingMode MouseTracking
        => _model.Terminal?.Engine?.MouseTrackingMode ?? MouseTrackingMode.None;

    /// <summary>
    /// Процесс рисует на альтернативном экране — своей прокрутки у тайла нет.
    /// </summary>
    private bool IsAlternateScreen => _model.Terminal?.IsAlternateBufferActive == true;

    /// <summary>
    /// Прокручивает полноэкранное приложение колесом мыши, переводя поворот в
    /// нажатия стрелок.
    /// </summary>
    /// <param name="delta">
    /// Вертикальная составляющая поворота колеса.
    /// </param>
    /// <returns>
    /// false, если поворот принадлежит не нам: прокрутку ведёт либо сам
    /// терминал, либо процесс, который следит за мышью.
    /// </returns>
    /// <remarks>
    /// Подробности перевода — в <see cref="AlternateScroll"/>.
    /// </remarks>
    public bool ScrollAlternateScreen(double delta)
    {
        if (!IsAlternateScreen || MouseTracking is not MouseTrackingMode.None)
        {
            return false;
        }

        var keys = AlternateScroll.Keys(delta, _rows, _model.Terminal?.Engine?.ApplicationCursorKeys == true);

        if (keys.Length == 0)
        {
            return false;
        }

        _model.Send(keys);
        return true;
    }

    /// <summary>
    /// Выключает отслеживание мыши на время выделения текста. Полноэкранные TUI
    /// (claude держит DECSET 1003 постоянно) забирают себе все нажатия, и
    /// терминалу нечего выделять: контрол отдаёт нажатие процессу и выделение не
    /// начинается, а нарисованное самим агентом выделение в буфер обмена не
    /// положить. Режим гасится в самом VT-парсере, процессу об этом не
    /// сообщается — он остаётся при своём мнении, а его собственный DECSET
    /// вернёт всё обратно и без нас.
    /// </summary>
    /// <returns>
    /// false, если гасить нечего: мышь и так принадлежит терминалу.
    /// </returns>
    public bool SuspendMouseTracking()
    {
        if (_suspendedMouseTracking is not MouseTrackingMode.None)
        {
            return true;
        }

        var mode = MouseTracking;

        if (mode is MouseTrackingMode.None)
        {
            return false;
        }

        _suspendedMouseTracking = mode;
        _model.Feed($"\u001b[?{(int)mode}l");
        return true;
    }

    /// <summary>
    /// Возвращает процессу отслеживание мыши после выделения. Если за время
    /// жеста процесс переставил режим сам, своё старое значение не навязываем:
    /// оно отправляло бы ему события мыши, которых он больше не ждёт.
    /// </summary>
    public void ResumeMouseTracking()
    {
        var mode = _suspendedMouseTracking;
        _suspendedMouseTracking = MouseTrackingMode.None;

        if (mode is MouseTrackingMode.None || MouseTracking is not MouseTrackingMode.None)
        {
            return;
        }

        _model.Feed($"\u001b[?{(int)mode}h");
    }

    /// <summary>
    /// Возвращает мышь терминалу перед запуском процесса в этом тайле.
    /// </summary>
    /// <remarks>
    /// Режим мыши предыдущего процесса новому не принадлежит: он его не просил,
    /// а получал бы в ввод и поворот колеса, и каждое движение мыши. Сам движок
    /// при сбросе этот режим оставляет себе, поэтому гасим его в потоке — так же,
    /// как на время выделения.
    /// </remarks>
    private void ReleaseMouseTracking()
    {
        _suspendedMouseTracking = MouseTrackingMode.None;

        var mode = MouseTracking;

        if (mode is not MouseTrackingMode.None)
        {
            _model.Feed($"\u001b[?{(int)mode}l");
        }
    }

    /// <summary>
    /// Запускает процесс по профилю. Повторный запуск сначала гасит предыдущий.
    /// </summary>
    public async Task StartAsync(AgentLaunchProfile profile, CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);

        Profile = profile;
        Interlocked.Exchange(ref _exitRaised, 0);

        ReleaseMouseTracking();

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
    /// <returns>
    /// null, если старт отменён гашением.
    /// </returns>
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
    /// Возвращает последние заполненные строки живого экрана — вход детектора
    /// статусов.
    /// </summary>
    /// <remarks>
    /// Строки берутся от начала активного экрана, а не от видимой области:
    /// пользователь может уйти в прокрутку, а процесс всё равно пишет вниз
    /// буфера. Иначе состояние читалось бы по чужому месту истории — старое
    /// «esc to interrupt», попавшее в вид, держало бы лампочку мигающей, пока
    /// тайл прокручен.
    /// Хвостовые пустые строки отбрасываются: codex и прочие встроенные в поток
    /// CLI рисуют интерфейс сразу под своим выводом, а не по низу экрана, и в
    /// свежем тайле их строка состояния («Working (0s • esc to interrupt)»)
    /// оказывается в верхней трети. Окно, отсчитанное от низа экрана, видело бы
    /// в таком тайле одну пустоту, и статус агента остался бы неразобранным.
    /// </remarks>
    public IReadOnlyList<string> SnapshotLastRows(int count)
    {
        if (count <= 0 || _model.Terminal is not { } terminal)
        {
            return [];
        }

        try
        {
            var buffer = terminal.Buffer;
            var rows = Math.Min(terminal.Rows, buffer.Lines.Length - buffer.YBase);
            var lines = new List<string>(Math.Max(rows, 0));

            for (var row = 0; row < rows; row++)
            {
                lines.Add(buffer.GetLine(buffer.YBase + row) is { } line
                    ? line.TranslateToString(true, 0, line.Length)
                    : string.Empty);
            }

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return lines.Count > count ? lines[^count..] : lines;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NullReferenceException or ArgumentOutOfRangeException)
        {
            // Буфер перестраивается параллельно с ресайзом — снимок пропускаем.
            return [];
        }
    }

    /// <summary>
    /// Выделяет весь буфер вместе с прокруткой.
    /// </summary>
    public void SelectAll() => _model.SelectAll();

    /// <summary>
    /// Снимает выделение.
    /// </summary>
    public void ClearSelection() => _model.ClearSelection();

    /// <summary>
    /// Отдаёт процессу текст из буфера обмена.
    /// </summary>
    /// <param name="text">
    /// Текст из буфера обмена.
    /// </param>
    /// <returns>
    /// false, если вставлять нечего или процесс уже не читает ввод.
    /// </returns>
    public bool Paste(string? text)
    {
        // Мёртвый процесс ввод не забирает: вставка в завершённый или упавший
        // тайл ушла бы в никуда, не оставив на экране следа.
        if (!IsRunning)
        {
            return false;
        }

        var payload = PasteText.Prepare(text, IsBracketedPaste);

        if (payload.Length == 0)
        {
            return false;
        }

        // Ввод снимает выделение — так делает и сам контрол, когда нажатие
        // доходит до него. Вставку он не видит: сочетание погашено, а Send
        // выделения не касается. Оставленное выделение сделало бы следующий
        // Ctrl+C копированием, отобрав у пользователя прерывание процесса.
        _model.ClearSelection();
        _model.Send(payload);
        return true;
    }

    /// <summary>
    /// Полностью очищает экран и scrollback перед перезапуском в том же тайле.
    /// </summary>
    public void Reset()
    {
        _output.Clear();
        _marginScrollback.Reset();
        _model.ClearSelection();
        ReleaseMouseTracking();
        _model.Terminal?.Engine?.Reset();
        DropScrollback();
        _model.FullBufferUpdate();
    }

    /// <summary>
    /// Выбрасывает прокрутку прошлого процесса.
    /// </summary>
    /// <remarks>
    /// Сброс движка затирает текст строк, но их число и начало экрана оставляет
    /// как было: перезапущенный тайл показывал бы живую полосу прокрутки, а за
    /// ней — пустоту во всю прошлую историю. Отдельного способа обрезать
    /// прокрутку у буфера нет, зато пересчёт по прежним размерам сам добирает
    /// строки до экрана и прижимает и начало экрана, и вид к нулю.
    /// </remarks>
    private void DropScrollback()
    {
        if (_model.Terminal is not { } terminal)
        {
            return;
        }

        var buffer = terminal.Buffer;

        buffer.Lines.Clear();
        buffer.Resize(terminal.Cols, terminal.Rows);
        buffer.SetCursor(0, 0);
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
            _marginScrollback.Feed(chunk, chunk.Length);
        }

        // Инверсия правится после разбора, поэтому экран, собранный внутри Feed,
        // о ней ещё не знает — изменившимся ячейкам нужен второй проход.
        if (_inverseVideo.Normalize())
        {
            _model.UpdateDisplay();
        }
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
