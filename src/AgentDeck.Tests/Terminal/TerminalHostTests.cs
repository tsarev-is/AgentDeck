using System.Text;
using AgentDeck.Models;
using AgentDeck.Terminal;
using NUnit.Framework;
using XTerm.Input;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Хост терминала на живом PTY: согласование старта с гашением.
/// </summary>
[TestFixture]
[Platform("Linux")]
public class TerminalHostTests
{
    /// <summary>
    /// Старт, опоздавший к гашению хоста, не оставляет процесса: погашенный
    /// хост уже не попадёт ни в StopAsync, ни в общий ShutdownAsync, и такой
    /// процесс жил бы до конца сессии пользователя.
    /// </summary>
    [Test]
    public async Task StartAfterDispose_LeavesNoProcess()
    {
        var marker = $"agentdeck-host-{Guid.NewGuid():N}";
        var host = new TerminalHost();

        await host.DisposeAsync();
        await host.StartAsync(AgentLaunchProfile.Create(
            AgentKind.Script,
            $"sleep 300 # {marker}",
            Path.GetTempPath()));

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(host.IsRunning, Is.False, "погашенный хост не должен получить сессию");
                Assert.That(CountProcesses(marker), Is.Zero, "процесс не должен пережить гашение");
            });
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Живой хост отдаёт pid своего процесса — по нему тайл спрашивает рабочую
    /// директорию. Погашенный не отдаёт ничего: номер мёртвого процесса система
    /// вправе выдать кому угодно, и опрос ушёл бы к чужой директории.
    /// </summary>
    [Test]
    public async Task Pid_FollowsProcessLifetime()
    {
        var directory = Directory.CreateTempSubdirectory("agentdeck-host-");
        var host = new TerminalHost();

        try
        {
            await host.StartAsync(AgentLaunchProfile.Create(AgentKind.Script, string.Empty, directory.FullName));

            Assert.Multiple(() =>
            {
                Assert.That(host.Pid, Is.Not.Null, "запущенный хост знает свой процесс");
                Assert.That(
                    ProcessDirectory.ReadForeground(host.Pid ?? 0),
                    Is.EqualTo(directory.FullName),
                    "процесс тайла стартует в директории запуска");
            });
        }
        finally
        {
            await host.DisposeAsync();
            directory.Delete(recursive: true);
        }

        Assert.That(host.Pid, Is.Null, "у погашенного хоста процесса нет");
    }

    /// <summary>
    /// Вставка в тайл без живого процесса не делается: ввод забирать некому, а
    /// экранная модель приняла бы текст, показав его отправленным.
    /// </summary>
    [Test]
    public async Task Paste_WithoutProcess_DoesNothing()
    {
        var host = new TerminalHost();

        try
        {
            Assert.That(host.Paste("text"), Is.False);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Вставка снимает выделение. Сам контрол снять его не успевает: сочетание
    /// вставки погашено и до него не доходит, а Send выделения не касается.
    /// Оставленное выделение сделало бы следующий Ctrl+C копированием, отобрав
    /// у пользователя единственный способ прервать процесс.
    /// </summary>
    [Test]
    [Platform("Linux")]
    public async Task Paste_ClearsSelection()
    {
        var host = new TerminalHost();

        try
        {
            await host.StartAsync(AgentLaunchProfile.Create(AgentKind.Script, "sleep 300", Path.GetTempPath()));

            // Выделять нужно непустой буфер, а вывод процесса доезжает до модели
            // через диспетчер, которого в тесте никто не крутит: пишем в модель
            // напрямую.
            var line = "selected"u8.ToArray();
            host.Model.Feed(line, line.Length);
            host.SelectAll();

            Assert.That(host.HasSelection, Is.True, "выделен весь буфер");
            Assert.That(host.Paste("text"), Is.True, "живой процесс забирает вставку");
            Assert.That(host.HasSelection, Is.False);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Выделение мышью в полноэкранном TUI возможно, только пока терминал не
    /// отдаёт нажатия процессу: claude держит отслеживание мыши постоянно, и без
    /// снятия режима выделять было бы нечего. Процесс о снятии не знает, поэтому
    /// после жеста режим обязан вернуться сам.
    /// </summary>
    [Test]
    public async Task SuspendMouseTracking_HoldsModeUntilResume()
    {
        var host = new TerminalHost();

        try
        {
            host.Model.Feed("\u001b[?1003h");

            Assert.That(MouseTracking(host), Is.EqualTo(MouseTrackingMode.AnyEvent), "процесс включил отслеживание мыши");
            Assert.That(host.SuspendMouseTracking(), Is.True, "снимать было что");
            Assert.That(MouseTracking(host), Is.EqualTo(MouseTrackingMode.None), "на время выделения мышь у терминала");

            host.ResumeMouseTracking();

            Assert.That(MouseTracking(host), Is.EqualTo(MouseTrackingMode.AnyEvent), "после жеста мышь возвращается процессу");
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Когда мышь и так принадлежит терминалу (shell без TUI), снимать нечего:
    /// представление по этому ответу отличает свой жест от обычной протяжки.
    /// </summary>
    [Test]
    public async Task SuspendMouseTracking_WithoutTracking_ReportsNothing()
    {
        var host = new TerminalHost();

        try
        {
            Assert.That(host.SuspendMouseTracking(), Is.False);

            host.ResumeMouseTracking();

            Assert.That(MouseTracking(host), Is.EqualTo(MouseTrackingMode.None), "своего режима у нас нет и навязывать нечего");
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Если процесс переставил режим мыши, пока шло выделение, старое значение
    /// ему не навязывается: оно отправляло бы процессу события мыши, которых он
    /// в новом режиме не ждёт.
    /// </summary>
    [Test]
    public async Task ResumeMouseTracking_KeepsModeChosenByProcess()
    {
        var host = new TerminalHost();

        try
        {
            host.Model.Feed("\u001b[?1003h");
            host.SuspendMouseTracking();
            host.Model.Feed("\u001b[?1000h");
            host.ResumeMouseTracking();

            Assert.That(MouseTracking(host), Is.EqualTo(MouseTrackingMode.VT200));
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Перезапуск в том же тайле начинается с чистого буфера. Сброс движка сам
    /// только затирает текст строк: их число и начало экрана остаются прежними,
    /// и тайл показывал бы живую полосу прокрутки, а за ней — пустоту во всю
    /// историю прошлого процесса.
    /// </summary>
    [Test]
    public async Task Reset_AfterScrollback_LeavesNothingToScroll()
    {
        var host = new TerminalHost();

        try
        {
            for (var line = 0; line < 60; line++)
            {
                host.Model.Feed($"line {line}\r\n");
            }

            var buffer = host.Model.Terminal.Buffer;

            Assert.That(buffer.YBase, Is.GreaterThan(0), "вывод должен наполнить прокрутку");

            host.Reset();

            Assert.Multiple(() =>
            {
                Assert.That(buffer.YBase, Is.Zero, "прокрутка выброшена");
                Assert.That(buffer.Lines.Length, Is.EqualTo(host.Model.Terminal.Rows), "в буфере остался ровно экран");
                Assert.That(host.Model.CanScroll, Is.False, "полосе прокрутки нечего показывать");
                Assert.That(host.HasSelection, Is.False, "выделение прошлого процесса снято");
            });
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Полноэкранное приложение (так рисует claude) не оставляет тайлу ни строки
    /// прокрутки: экран оно переписывает целиком, ушедшего вверх текста терминал
    /// не видит. Отсюда и пустая полоса прокрутки в таком тайле — прокручивать
    /// нечего, и подменить это нечем.
    /// </summary>
    [Test]
    public async Task AlternateScreen_AfterFullScreenOutput_HasNothingToScroll()
    {
        var host = new TerminalHost();

        try
        {
            host.Model.Feed("\u001b[?1049h");

            for (var line = 0; line < 200; line++)
            {
                host.Model.Feed($"\u001b[{(line % 24) + 1};1Hline {line}");
            }

            Assert.Multiple(() =>
            {
                Assert.That(host.Model.Terminal.IsAlternateBufferActive, Is.True, "экран альтернативный");
                Assert.That(host.Model.MaxScrollback, Is.Zero, "прокрутки у такого экрана нет");
                Assert.That(host.Model.CanScroll, Is.False, "полосе прокрутки нечего показывать");
            });
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Колесо на альтернативном экране уходит приложению стрелками — так его
    /// прокручивают настоящие терминалы, и только так прокручиваются less, man и
    /// <c>git log</c> в тайле.
    /// </summary>
    [Test]
    public async Task ScrollAlternateScreen_OnAlternateScreen_SendsCursorKeys()
    {
        var host = new TerminalHost();
        var sent = new StringBuilder();

        try
        {
            host.Model.UserInput += (_, e) => sent.Append(Encoding.UTF8.GetString(e.Data.Span));
            host.Model.Feed("\u001b[?1049h");

            Assert.Multiple(() =>
            {
                Assert.That(host.ScrollAlternateScreen(1), Is.True, "поворот принят");
                Assert.That(sent.ToString(), Is.EqualTo("\u001b[A"), "ушла стрелка вверх");
            });
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Приложение, которое следит за мышью само (claude), колесо получает
    /// событиями мыши, и стрелки ему посылать нельзя: в его поле ввода они
    /// перебирают историю запросов вместо прокрутки.
    /// </summary>
    [Test]
    public async Task ScrollAlternateScreen_WhenProcessTracksMouse_SendsNothing()
    {
        var host = new TerminalHost();
        var sent = new StringBuilder();

        try
        {
            host.Model.UserInput += (_, e) => sent.Append(Encoding.UTF8.GetString(e.Data.Span));
            host.Model.Feed("\u001b[?1049h\u001b[?1003h");

            Assert.Multiple(() =>
            {
                Assert.That(host.ScrollAlternateScreen(1), Is.False, "поворот принадлежит процессу");
                Assert.That(sent.ToString(), Is.EqualTo(string.Empty), "процессу ничего не ушло");
            });
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// На обычном экране прокрутка — своя: там у тайла есть буфер, и подменять её
    /// стрелками значило бы отдать процессу чужое нажатие.
    /// </summary>
    [Test]
    public async Task ScrollAlternateScreen_OnNormalScreen_SendsNothing()
    {
        var host = new TerminalHost();
        var sent = new StringBuilder();

        try
        {
            host.Model.UserInput += (_, e) => sent.Append(Encoding.UTF8.GetString(e.Data.Span));

            for (var line = 0; line < 60; line++)
            {
                host.Model.Feed($"line {line}\r\n");
            }

            Assert.Multiple(() =>
            {
                Assert.That(host.ScrollAlternateScreen(1), Is.False, "прокрутку ведёт сам терминал");
                Assert.That(sent.ToString(), Is.EqualTo(string.Empty), "процессу ничего не ушло");
                Assert.That(host.Model.CanScroll, Is.True, "и прокручивать есть что");
            });
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Снимок для детектора статусов берётся от последней заполненной строки, а
    /// не от низа экрана: codex рисует своё «Working (0s • esc to interrupt)»
    /// сразу под выводом, и в свежем тайле под этой строкой остаётся полэкрана
    /// пустоты — окно по низу экрана не увидело бы состояния агента вовсе.
    /// </summary>
    [Test]
    public async Task SnapshotLastRows_SkipsEmptyTailOfScreen()
    {
        var host = new TerminalHost();

        try
        {
            host.Model.Feed("> count from 1 to 30\r\n• Working (0s • esc to interrupt)\r\n");

            var rows = host.SnapshotLastRows(2);

            Assert.Multiple(() =>
            {
                Assert.That(rows, Has.Count.EqualTo(2), "пустой хвост экрана в снимок не идёт");
                Assert.That(rows[^1].TrimEnd(), Is.EqualTo("• Working (0s • esc to interrupt)"));
            });
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Пустой экран даёт пустой снимок, а не строки из пробелов: детектору не за
    /// что зацепиться, и он остаётся при прежнем статусе.
    /// </summary>
    [Test]
    public async Task SnapshotLastRows_OnEmptyScreen_ReturnsNothing()
    {
        var host = new TerminalHost();

        try
        {
            Assert.That(host.SnapshotLastRows(30), Is.Empty);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Пока пользователь читает прокрутку, состояние снимается всё равно с живого
    /// экрана: процесс пишет вниз буфера, а не в вид. Иначе старое
    /// «esc to interrupt», попавшее в вид, держало бы лампочку мигающей всё
    /// время, что тайл прокручен, — а прокрутка у таких тайлов и появилась-то
    /// недавно.
    /// </summary>
    [Test]
    public async Task SnapshotLastRows_WhileScrolledBack_ReadsLiveScreen()
    {
        var host = new TerminalHost();

        try
        {
            for (var line = 0; line < 60; line++)
            {
                host.Model.Feed($"line {line}\r\n");
            }

            host.Model.Feed("• Working (3s • esc to interrupt)");
            host.Model.ScrollLines(-40);

            Assert.Multiple(() =>
            {
                Assert.That(host.Model.Terminal.Buffer.YDisp, Is.LessThan(host.Model.Terminal.Buffer.YBase), "тайл прокручен вверх");
                Assert.That(
                    host.SnapshotLastRows(3).Select(row => row.TrimEnd()),
                    Contains.Item("• Working (3s • esc to interrupt)"),
                    "снимок остался с живого экрана");
            });
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Перезапуск в том же тайле возвращает мышь терминалу: режим отслеживания
    /// прошлого процесса движок при сбросе оставляет себе, и новый процесс, его
    /// не просивший, получал бы в ввод и поворот колеса, и каждое движение мыши.
    /// </summary>
    [Test]
    public async Task Reset_AfterProcessTrackedMouse_ReturnsMouseToTerminal()
    {
        var host = new TerminalHost();

        try
        {
            host.Model.Feed("\u001b[?1049h\u001b[?1003h");

            Assert.That(MouseTracking(host), Is.EqualTo(MouseTrackingMode.AnyEvent), "процесс забрал мышь");

            host.Reset();

            Assert.Multiple(() =>
            {
                Assert.That(MouseTracking(host), Is.EqualTo(MouseTrackingMode.None), "мышь вернулась терминалу");
                Assert.That(host.Model.IsMouseModeActive, Is.False, "и контрол больше не считает её чужой");
                Assert.That(host.SuspendMouseTracking(), Is.False, "гасить на время выделения уже нечего");
            });
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Режим отслеживания мыши, который видит VT-парсер хоста.
    /// </summary>
    private static MouseTrackingMode MouseTracking(TerminalHost host)
        => host.Model.Terminal?.Engine?.MouseTrackingMode ?? MouseTrackingMode.None;

    /// <summary>
    /// Считает живые процессы, в командной строке которых есть маркер.
    /// </summary>
    private static int CountProcesses(string marker)
    {
        var count = 0;

        foreach (var directory in Directory.GetDirectories("/proc"))
        {
            try
            {
                var cmdline = File.ReadAllText(Path.Combine(directory, "cmdline")).Replace('\0', ' ');

                if (cmdline.Contains(marker, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // Процесс успел исчезнуть или принадлежит другому пользователю.
            }
        }

        return count;
    }
}
