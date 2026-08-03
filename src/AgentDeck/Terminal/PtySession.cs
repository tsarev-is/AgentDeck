using System.Text;
using Porta.Pty;

namespace AgentDeck.Terminal;

/// <summary>
/// Обёртка над PTY-процессом: фоновое чтение вывода, запись ввода, ресайз
/// с debounce и надёжное завершение процесса.
/// </summary>
public sealed class PtySession : IAsyncDisposable
{
    private const int ReadBufferSize = 8192;

    private readonly IPtyConnection _connection;
    private readonly Action<byte[]> _onOutput;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ResizeDebouncer _resize;
    private readonly Timer _resizeTimer;
    private readonly Task _readLoop;

    private int _exited;

    private PtySession(IPtyConnection connection, Action<byte[]> onOutput, Action? onExited)
    {
        _connection = connection;
        _onOutput = onOutput;

        _resize = new ResizeDebouncer(
            ApplyResize,
            confirmations: OperatingSystem.IsWindows() ? 1 : 0);

        // Тик вдвое чаще окна схлопывания: задержка отправки не превышает 1.5 окна.
        var period = ResizeDebouncer.DefaultDelay / 2;
        _resizeTimer = new Timer(_ => _resize.Tick(), null, period, period);

        _connection.ProcessExited += (_, e) => RaiseExited(e.ExitCode);
        _readLoop = Task.Run(() => ReadLoopAsync(onExited));
    }

    /// <summary>
    /// Процесс завершился; аргумент — код возврата.
    /// </summary>
    public event EventHandler<int>? Exited;

    /// <summary>
    /// Идентификатор процесса.
    /// </summary>
    public int Pid => _connection.Pid;

    /// <summary>
    /// Код возврата, если процесс уже завершился.
    /// </summary>
    public int? ExitCode { get; private set; }

    /// <summary>
    /// Процесс ещё жив.
    /// </summary>
    public bool IsRunning => ExitCode is null;

    /// <summary>
    /// Запускает процесс по профилю в PTY указанного размера.
    /// </summary>
    /// <param name="profile">
    /// Что и где запускать.
    /// </param>
    /// <param name="cols">
    /// Ширина PTY в символах.
    /// </param>
    /// <param name="rows">
    /// Высота PTY в строках.
    /// </param>
    /// <param name="onOutput">
    /// Приём вывода; вызывается из фонового потока.
    /// </param>
    /// <param name="onExited">
    /// Вызывается при обрыве потока вывода.
    /// </param>
    /// <param name="cancellationToken">
    /// Отмена запуска.
    /// </param>
    public static async Task<PtySession> StartAsync(
        AgentLaunchProfile profile,
        int cols,
        int rows,
        Action<byte[]> onOutput,
        Action? onExited = null,
        CancellationToken cancellationToken = default)
    {
        var options = new PtyOptions
        {
            Name = "AgentDeck",
            Cols = Math.Max(1, cols),
            Rows = Math.Max(1, rows),
            Cwd = profile.WorkingDirectory,
            App = profile.App,
            CommandLine = [.. profile.CommandLine],
            Environment = profile.Environment.ToDictionary(p => p.Key, p => p.Value),
        };

        var connection = await PtyProvider.SpawnAsync(options, cancellationToken).ConfigureAwait(false);
        return new PtySession(connection, onOutput, onExited);
    }

    /// <summary>
    /// Пишет байты во ввод процесса.
    /// </summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (!IsRunning || data.IsEmpty)
        {
            return;
        }

        try
        {
            _connection.WriterStream.Write(data);
            _connection.WriterStream.Flush();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Процесс уже закрыл свой конец PTY — считаем, что он завершается.
        }
    }

    /// <summary>
    /// Пишет текст во ввод процесса в UTF-8.
    /// </summary>
    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// Планирует ресайз PTY; фактическая отправка проходит через debounce.
    /// </summary>
    public void Resize(int cols, int rows) => _resize.Request(cols, rows);

    /// <summary>
    /// Немедленно завершает процесс.
    /// </summary>
    public void Kill()
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            _connection.Kill();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // Процесс успел завершиться сам.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);
        await _resizeTimer.DisposeAsync().ConfigureAwait(false);

        Kill();

        try
        {
            await _readLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            // Поток чтения завис на закрытом дескрипторе — отпускаем его.
        }

        try
        {
            _connection.Dispose();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // PTY уже закрыт вместе с процессом — освобождать нечего, и падать
            // на этом нельзя: dispose зовут в том числе при выходе из приложения.
        }

        _cancellation.Dispose();
    }

    private void ApplyResize(int cols, int rows)
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            _connection.Resize(Math.Max(1, cols), Math.Max(1, rows));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // Ресайз мёртвого PTY игнорируется.
        }
    }

    private async Task ReadLoopAsync(Action? onExited)
    {
        var buffer = new byte[ReadBufferSize];

        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var read = await _connection.ReaderStream
                    .ReadAsync(buffer.AsMemory(), _cancellation.Token)
                    .ConfigureAwait(false);

                if (read <= 0)
                {
                    break;
                }

                _onOutput(buffer[..read]);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Нормальный конец жизни PTY: дескриптор закрылся вместе с процессом.
        }
        finally
        {
            RaiseExited(TryReadExitCode());
            onExited?.Invoke();
        }
    }

    private int TryReadExitCode()
    {
        try
        {
            _connection.WaitForExit(1000);
            return _connection.ExitCode;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return 0;
        }
    }

    private void RaiseExited(int exitCode)
    {
        if (Interlocked.Exchange(ref _exited, 1) != 0)
        {
            return;
        }

        ExitCode = exitCode;
        Exited?.Invoke(this, exitCode);
    }
}
