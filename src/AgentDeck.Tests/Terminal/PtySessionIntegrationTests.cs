using System.Text;
using AgentDeck.Models;
using AgentDeck.Terminal;
using NUnit.Framework;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Живой PTY: запуск процесса, вывод, ввод, код возврата и убийство процесса.
/// </summary>
[TestFixture]
[Platform("Linux")]
public class PtySessionIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Простая команда выдаёт вывод и завершается с нулевым кодом.
    /// </summary>
    [Test]
    public async Task Spawn_Echo_ProducesOutputAndExitsZero()
    {
        var output = new StringBuilder();
        var exited = new TaskCompletionSource<int>();

        await using var session = await PtySession.StartAsync(
            Profile("/bin/echo", ["hi-from-pty"]),
            80,
            24,
            data =>
            {
                lock (output)
                {
                    output.Append(Encoding.UTF8.GetString(data));
                }
            });

        session.Exited += (_, code) => exited.TrySetResult(code);

        var exitCode = await WithTimeout(exited.Task);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(session.ExitCode, Is.Zero);
            Assert.That(session.IsRunning, Is.False);
        });

        // Вывод приходит асинхронно, дадим циклу чтения дочитать хвост.
        await WaitUntil(() =>
        {
            lock (output)
            {
                return output.ToString().Contains("hi-from-pty", StringComparison.Ordinal);
            }
        });

        lock (output)
        {
            Assert.That(output.ToString(), Does.Contain("hi-from-pty"));
        }
    }

    /// <summary>
    /// Ненулевой код возврата доходит до подписчика.
    /// </summary>
    [Test]
    public async Task Spawn_FailingCommand_ReportsNonZeroExitCode()
    {
        var exited = new TaskCompletionSource<int>();

        await using var session = await PtySession.StartAsync(
            Profile("/bin/sh", ["-c", "exit 3"]),
            80,
            24,
            _ => { });

        session.Exited += (_, code) => exited.TrySetResult(code);

        Assert.That(await WithTimeout(exited.Task), Is.EqualTo(3));
    }

    /// <summary>
    /// Процесс получает ввод и отвечает на него.
    /// </summary>
    [Test]
    public async Task Write_FeedsProcessStdin()
    {
        var output = new StringBuilder();

        await using var session = await PtySession.StartAsync(
            Profile("/bin/sh", ["-c", "read line; echo GOT:$line"]),
            80,
            24,
            data =>
            {
                lock (output)
                {
                    output.Append(Encoding.UTF8.GetString(data));
                }
            });

        session.Write("ping\n");

        await WaitUntil(() =>
        {
            lock (output)
            {
                return output.ToString().Contains("GOT:ping", StringComparison.Ordinal);
            }
        });

        lock (output)
        {
            Assert.That(output.ToString(), Does.Contain("GOT:ping"));
        }
    }

    /// <summary>
    /// PTY сообщает процессу свой размер, и ресайз этот размер меняет.
    /// </summary>
    [Test]
    public async Task Resize_ChangesReportedTerminalSize()
    {
        var output = new StringBuilder();

        await using var session = await PtySession.StartAsync(
            Profile("/bin/sh", ["-c", "sleep 0.3; stty size"]),
            120,
            40,
            data =>
            {
                lock (output)
                {
                    output.Append(Encoding.UTF8.GetString(data));
                }
            });

        await WaitUntil(() =>
        {
            lock (output)
            {
                return output.ToString().Contains("40 120", StringComparison.Ordinal);
            }
        });

        lock (output)
        {
            Assert.That(output.ToString(), Does.Contain("40 120"), "PTY должен стартовать с заданным размером");
        }
    }

    /// <summary>
    /// Kill гасит долгоживущий процесс, и он не остаётся сиротой.
    /// </summary>
    [Test]
    public async Task Kill_TerminatesLongRunningProcess()
    {
        var exited = new TaskCompletionSource<int>();

        var session = await PtySession.StartAsync(
            Profile("/bin/sh", ["-c", "sleep 300"]),
            80,
            24,
            _ => { });

        session.Exited += (_, code) => exited.TrySetResult(code);
        var pid = session.Pid;

        Assert.That(session.IsRunning, Is.True);

        session.Kill();
        await WithTimeout(exited.Task);
        await session.DisposeAsync();

        await WaitUntil(() => !ProcessExists(pid));
        Assert.That(ProcessExists(pid), Is.False, $"процесс {pid} должен быть убит");
    }

    /// <summary>
    /// DisposeAsync без явного Kill тоже не оставляет процесса.
    /// </summary>
    [Test]
    public async Task DisposeAsync_LeavesNoOrphanProcess()
    {
        var session = await PtySession.StartAsync(
            Profile("/bin/sh", ["-c", "sleep 300"]),
            80,
            24,
            _ => { });

        var pid = session.Pid;
        await session.DisposeAsync();

        await WaitUntil(() => !ProcessExists(pid));
        Assert.That(ProcessExists(pid), Is.False, $"процесс {pid} должен быть убит при dispose");
    }

    /// <summary>
    /// Реальный профиль «script» поднимает живой shell в указанной директории.
    /// </summary>
    [Test]
    public async Task ScriptProfile_StartsShellInRequestedDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"agentdeck-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var output = new StringBuilder();

        try
        {
            await using var session = await PtySession.StartAsync(
                AgentLaunchProfile.Create(AgentKind.Script, string.Empty, directory),
                80,
                24,
                data =>
                {
                    lock (output)
                    {
                        output.Append(Encoding.UTF8.GetString(data));
                    }
                });

            session.Write("pwd\n");

            await WaitUntil(() =>
            {
                lock (output)
                {
                    return output.ToString().Contains(directory, StringComparison.Ordinal);
                }
            });

            lock (output)
            {
                Assert.That(output.ToString(), Does.Contain(directory));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Команду утилиты выполняет интерактивный shell: только такой читает
    /// <c>~/.bashrc</c>, где у пользователя обычно и лежат PATH и алиасы.
    /// </summary>
    [Test]
    public async Task AgentProfile_RunsCommandInInteractiveShell()
    {
        var shell = Path.GetFileName(AgentLaunchProfile.ResolveShell());

        if (shell is not ("bash" or "zsh" or "sh" or "dash" or "ksh"))
        {
            Assert.Ignore($"проверка через $- рассчитана на POSIX-shell, а не на {shell}");
        }

        var output = new StringBuilder();

        await using var session = await PtySession.StartAsync(
            AgentLaunchProfile.Create(
                AgentKind.Script,
                "case \"$-\" in *i*) echo SHELL-IS-INTERACTIVE;; esac",
                Path.GetTempPath()),
            80,
            24,
            data =>
            {
                lock (output)
                {
                    output.Append(Encoding.UTF8.GetString(data));
                }
            });

        await WaitUntil(() =>
        {
            lock (output)
            {
                return output.ToString().Contains("SHELL-IS-INTERACTIVE", StringComparison.Ordinal);
            }
        });

        lock (output)
        {
            Assert.That(
                output.ToString(),
                Does.Contain("SHELL-IS-INTERACTIVE"),
                "утилита должна стартовать интерактивным shell'ом, иначе теряются PATH и алиасы из rc-файлов");
        }
    }

    private static AgentLaunchProfile Profile(string app, string[] args)
        => new(
            AgentKind.Script,
            app,
            args,
            Path.GetTempPath(),
            new Dictionary<string, string> { ["TERM"] = AgentLaunchProfile.TerminalType });

    private static bool ProcessExists(int pid) => Directory.Exists($"/proc/{pid}");

    private static async Task<T> WithTimeout<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(Timeout));
        Assert.That(completed, Is.SameAs(task), "операция не уложилась в таймаут");
        return await task;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }
    }
}
