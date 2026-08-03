using System.Diagnostics;
using AgentDeck.Models;
using AgentDeck.Terminal;
using NUnit.Framework;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Чтение рабочей директории процесса: разбор «/proc/&lt;pid&gt;/stat» и живой «/proc».
/// </summary>
[TestFixture]
public class ProcessDirectoryTests
{
    /// <summary>
    /// tpgid берётся из восьмого поля stat.
    /// </summary>
    [Test]
    public void ParseForegroundGroup_ReadsTpgid()
    {
        const string stat = "4242 (bash) S 4240 4242 4242 34816 5150 4194304 3521 0 0 0 8 3 0 0 20 0 1 0 27863007";

        Assert.That(ProcessDirectory.ParseForegroundGroup(stat), Is.EqualTo(5150));
    }

    /// <summary>
    /// Имя процесса в stat не экранируется: пробелы и скобки внутри него сдвинули
    /// бы разбор по полям, и вместо tpgid прочиталось бы что попало.
    /// </summary>
    [TestCase("77 (weird name) S 1 77 77 34816 5150 4194304 0 0 0 0 0 0 0 0 20 0 1 0 1")]
    [TestCase("77 (in (brackets)) S 1 77 77 34816 5150 4194304 0 0 0 0 0 0 0 0 20 0 1 0 1")]
    [TestCase("77 ((()) S 1 77 77 34816 5150 4194304 0 0 0 0 0 0 0 0 20 0 1 0 1")]
    public void ParseForegroundGroup_SurvivesProcessName(string stat)
    {
        Assert.That(ProcessDirectory.ParseForegroundGroup(stat), Is.EqualTo(5150));
    }

    /// <summary>
    /// Процесс без управляющего терминала показывает tpgid -1 — группы нет.
    /// </summary>
    [Test]
    public void ParseForegroundGroup_WithoutTerminal_ReturnsNull()
    {
        const string stat = "4242 (dotnet) R 4240 4242 4242 0 -1 4194304 1693 0 0 0 2 0 0 0 20 0 8 0 27863007";

        Assert.That(ProcessDirectory.ParseForegroundGroup(stat), Is.Null);
    }

    /// <summary>
    /// Обрезанная или пустая строка stat не роняет разбор.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("4242 (bash) S 4240")]
    [TestCase("no brackets here")]
    public void ParseForegroundGroup_Malformed_ReturnsNull(string? stat)
    {
        Assert.That(ProcessDirectory.ParseForegroundGroup(stat), Is.Null);
    }

    /// <summary>
    /// Директория собственного процесса читается через «/proc».
    /// </summary>
    [Test]
    [Platform("Linux")]
    public void Read_OwnProcess_ReturnsCurrentDirectory()
    {
        var directory = ProcessDirectory.Read(Environment.ProcessId);

        Assert.That(directory, Is.EqualTo(Environment.CurrentDirectory));
    }

    /// <summary>
    /// У несуществующего процесса директории нет, но исключения тоже нет:
    /// опрашиватель наткнётся на мёртвый pid при каждом закрытии тайла.
    /// </summary>
    [Test]
    public void Read_UnknownProcess_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessDirectory.Read(-1), Is.Null);
            Assert.That(ProcessDirectory.Read(0), Is.Null);
            Assert.That(ProcessDirectory.Read(int.MaxValue), Is.Null);
            Assert.That(ProcessDirectory.ReadForeground(int.MaxValue), Is.Null);
        });
    }

    /// <summary>
    /// Каталог удалили из-под живого процесса: ядро продолжает отдавать прежний
    /// путь, приписав «(deleted)». Путём эта строка быть перестала, и наружу
    /// её выпускать нельзя — она дошла бы до заголовка тайла и до сессии, из
    /// которой тайл восстановился бы в несуществующем каталоге.
    /// </summary>
    [Test]
    [Platform("Linux")]
    public void Read_DeletedDirectory_ReturnsNull()
    {
        var directory = Directory.CreateTempSubdirectory("agentdeck-gone-");

        using var process = Process.Start(new ProcessStartInfo("sleep")
        {
            ArgumentList = { "300" },
            WorkingDirectory = directory.FullName,
            UseShellExecute = false,
        });

        Assert.That(process, Is.Not.Null, "процесс для опроса должен подняться");

        try
        {
            Assert.That(
                ProcessDirectory.Read(process.Id),
                Is.EqualTo(directory.FullName),
                "существующий каталог читается как есть");

            directory.Delete(recursive: true);

            Assert.That(ProcessDirectory.Read(process.Id), Is.Null);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();

            if (Directory.Exists(directory.FullName))
            {
                directory.Delete(recursive: true);
            }
        }
    }

    /// <summary>
    /// Живой shell в PTY: «cd» внутри него виден в рабочей директории процесса,
    /// а «cd» во вложенном shell'е — в директории группы переднего плана.
    /// </summary>
    [Test]
    [Platform("Linux")]
    public async Task ReadForeground_FollowsCdInsideShell()
    {
        var root = Directory.CreateTempSubdirectory("agentdeck-cwd-");
        var nested = root.CreateSubdirectory("nested");

        try
        {
            await using var session = await PtySession.StartAsync(
                AgentLaunchProfile.Create(AgentKind.Script, string.Empty, root.FullName),
                80,
                24,
                _ => { });

            Assert.That(
                await WaitForDirectory(session.Pid, root.FullName),
                Is.True,
                "shell стартует в директории запуска");

            session.Write($"cd '{nested.FullName}'\n");

            Assert.That(
                await WaitForDirectory(session.Pid, nested.FullName),
                Is.True,
                "«cd» в shell'е тайла меняет рабочую директорию процесса");

            // Вложенный shell забирает терминал себе: его «cd» владельцу PTY не
            // виден, и найти новый путь можно только по группе переднего плана.
            session.Write($"{AgentLaunchProfile.DefaultUnixShell} -i\n");
            session.Write($"cd '{root.FullName}'\n");

            Assert.That(
                await WaitForDirectory(session.Pid, root.FullName),
                Is.True,
                "«cd» во вложенном shell'е читается по группе переднего плана");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Ждёт, пока рабочая директория процесса не совпадёт с ожидаемой.
    /// </summary>
    /// <param name="pid">
    /// Идентификатор процесса, поднятого в PTY.
    /// </param>
    /// <param name="expected">
    /// Ожидаемая директория.
    /// </param>
    /// <returns>
    /// false, если директория так и не совпала за отведённое время.
    /// </returns>
    private static async Task<bool> WaitForDirectory(int pid, string expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            if (string.Equals(ProcessDirectory.ReadForeground(pid), expected, StringComparison.Ordinal))
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }
}
