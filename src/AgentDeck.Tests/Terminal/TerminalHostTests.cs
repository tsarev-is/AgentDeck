using AgentDeck.Models;
using AgentDeck.Terminal;
using NUnit.Framework;

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
