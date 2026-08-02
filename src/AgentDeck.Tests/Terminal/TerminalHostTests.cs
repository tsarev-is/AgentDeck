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
