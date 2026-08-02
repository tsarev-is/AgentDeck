using AgentDeck.Settings;
using NUnit.Framework;

namespace AgentDeck.Tests.Settings;

/// <summary>
/// Предстартовая проверка команды: пути, PATH, shell-синтаксис и запасной
/// опрос shell.
/// </summary>
[TestFixture]
public class CommandResolverTests
{
    private string _binDirectory = null!;
    private string _executable = null!;

    [SetUp]
    public void SetUp()
    {
        _binDirectory = Path.Combine(Path.GetTempPath(), $"agentdeck-bin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_binDirectory);

        _executable = Path.Combine(_binDirectory, "fakeagent");
        File.WriteAllText(_executable, "#!/bin/sh\nexit 0\n");

        MakeExecutable(_executable);
    }

    /// <summary>
    /// Присваивания окружения — префикс команды, а не её имя: проверять надо
    /// то, что за ними, иначе документированное «FOO=bar cli» не запустится.
    /// </summary>
    [Test]
    public void FindMissingCommand_LeadingAssignments_ChecksRealExecutable()
    {
        var environment = new Dictionary<string, string> { ["PATH"] = _binDirectory };

        Assert.Multiple(() =>
        {
            Assert.That(
                NeverProbed().FindMissingCommand("FOO=bar LOG_LEVEL=2 fakeagent --flag", environment),
                Is.Null,
                "исполняемый файл за присваиваниями существует");

            Assert.That(
                new CommandResolver(_ => false).FindMissingCommand("FOO=bar ghostagent", environment),
                Is.EqualTo("ghostagent"),
                "в сообщении должна быть команда, а не присваивание");
        });
    }

    /// <summary>
    /// Команда из одних присваиваний ничего не запускает — проверять нечего.
    /// </summary>
    [Test]
    public void FindMissingCommand_OnlyAssignments_IsAllowed()
    {
        Assert.That(NeverProbed().FindMissingCommand("FOO=bar"), Is.Null);
    }

    /// <summary>
    /// Токен с «=» посреди имени — не присваивание, а обычная команда.
    /// </summary>
    [Test]
    public void FindMissingCommand_NonAssignmentToken_IsTreatedAsExecutable()
    {
        Assert.That(
            new CommandResolver(_ => false).FindMissingCommand("--opt=value", new Dictionary<string, string> { ["PATH"] = _binDirectory }),
            Is.EqualTo("--opt=value"));
    }

    /// <summary>
    /// Относительный путь считается от директории тайла: shell запустится
    /// именно там, а не в текущей директории приложения.
    /// </summary>
    [Test]
    public void FindMissingCommand_RelativePath_ResolvesFromWorkingDirectory()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                NeverProbed().FindMissingCommand("./fakeagent --full", environment: null, workingDirectory: _binDirectory),
                Is.Null,
                "скрипт лежит в директории тайла");

            Assert.That(
                NeverProbed().FindMissingCommand("./fakeagent", environment: null, workingDirectory: Path.GetTempPath()),
                Is.EqualTo("./fakeagent"),
                "в чужой директории его быть не должно");
        });
    }

    /// <summary>
    /// Ставит биту x на Unix; на Windows права файлов роли не играют.
    /// </summary>
    private static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_binDirectory))
        {
            Directory.Delete(_binDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Пустая команда — интерактивный shell, проверять нечего.
    /// </summary>
    [Test]
    public void FindMissingCommand_BlankCommand_IsAllowed([Values("", "   ", null)] string? command)
    {
        Assert.That(NeverProbed().FindMissingCommand(command), Is.Null);
    }

    /// <summary>
    /// Абсолютный путь к существующему исполняемому файлу проходит без опроса shell.
    /// </summary>
    [Test]
    public void FindMissingCommand_ExistingPath_IsAllowed()
    {
        Assert.That(NeverProbed().FindMissingCommand(_executable), Is.Null);
    }

    /// <summary>
    /// Несуществующий путь возвращается как ненайденная команда — shell не спрашиваем,
    /// путь указан явно.
    /// </summary>
    [Test]
    public void FindMissingCommand_MissingPath_ReturnsToken()
    {
        var path = Path.Combine(_binDirectory, "nope");

        Assert.That(NeverProbed().FindMissingCommand(path), Is.EqualTo(path));
    }

    /// <summary>
    /// Путь с «~» проверяется раскрытым, а в сообщение попадает как его набрали.
    /// </summary>
    [Test]
    public void FindMissingCommand_MissingTildePath_ReturnsTokenAsTyped()
    {
        var token = $"~/{Guid.NewGuid():N}/codex";

        Assert.That(NeverProbed().FindMissingCommand(token), Is.EqualTo(token));
    }

    /// <summary>
    /// «~» раскрывается в домашнюю директорию: путь под домашней папкой находится.
    /// </summary>
    [Test]
    [Platform("Linux,MacOsX")]
    public void FindMissingCommand_ExistingTildePath_IsAllowed()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directory = Path.Combine(home, $".agentdeck-test-{Guid.NewGuid():N}");
        var executable = Path.Combine(directory, "fakeagent");

        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(executable, "#!/bin/sh\nexit 0\n");
            MakeExecutable(executable);

            Assert.That(NeverProbed().FindMissingCommand($"~/{Path.GetFileName(directory)}/fakeagent"), Is.Null);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Аргументы команды на проверку не влияют — смотрим только на первый токен.
    /// </summary>
    [Test]
    public void FindMissingCommand_IgnoresArguments()
    {
        Assert.That(NeverProbed().FindMissingCommand($"{_executable} --full-auto --model gpt"), Is.Null);
    }

    /// <summary>
    /// Голая команда ищется в PATH запускаемого процесса.
    /// </summary>
    [Test]
    [Platform("Linux,MacOsX")]
    public void FindMissingCommand_FoundInEnvironmentPath_IsAllowed()
    {
        var environment = new Dictionary<string, string> { ["PATH"] = _binDirectory };

        Assert.That(NeverProbed().FindMissingCommand("fakeagent", environment), Is.Null);
    }

    /// <summary>
    /// Файл без бита x на Unix исполняемым не считается.
    /// </summary>
    [Test]
    [Platform("Linux,MacOsX")]
    public void FindMissingCommand_NonExecutableFile_ReturnsToken()
    {
        var data = Path.Combine(_binDirectory, "notes.txt");
        File.WriteAllText(data, "текст");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(data, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Assert.That(NeverProbed().FindMissingCommand(data), Is.EqualTo(data));
    }

    /// <summary>
    /// Команды в PATH нет — спрашиваем shell, и его «да» разрешает запуск.
    /// Ровно один опрос: PATH проверяется первым.
    /// </summary>
    [Test]
    public void FindMissingCommand_NotInPath_AsksShellOnce()
    {
        var probes = new List<string>();
        var resolver = new CommandResolver(token =>
        {
            probes.Add(token);
            return true;
        });

        var missing = resolver.FindMissingCommand("codex", new Dictionary<string, string> { ["PATH"] = _binDirectory });

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Null);
            Assert.That(probes, Is.EqualTo(new[] { "codex" }));
        });
    }

    /// <summary>
    /// Ни PATH, ни shell команду не знают — она и возвращается как ненайденная.
    /// </summary>
    [Test]
    public void FindMissingCommand_UnknownEverywhere_ReturnsToken()
    {
        var resolver = new CommandResolver(_ => false);

        var missing = resolver.FindMissingCommand("codex", new Dictionary<string, string> { ["PATH"] = _binDirectory });

        Assert.That(missing, Is.EqualTo("codex"));
    }

    /// <summary>
    /// Shell-выражение разбирать бессмысленно — пропускаем проверку целиком.
    /// </summary>
    [TestCase("npm run agent && codex")]
    [TestCase("cat log | grep error")]
    [TestCase("VAR=$HOME codex")]
    [TestCase("\"my agent\" --flag")]
    public void FindMissingCommand_ShellSyntax_IsSkipped(string command)
    {
        Assert.That(NeverProbed().FindMissingCommand(command, new Dictionary<string, string> { ["PATH"] = _binDirectory }), Is.Null);
    }

    /// <summary>
    /// Проверка, падающая при любом обращении к shell: доказывает, что путь
    /// быстрой проверки не спускается до запуска процесса.
    /// </summary>
    private static CommandResolver NeverProbed()
        => new(token => throw new AssertionException($"shell не должен опрашиваться для «{token}»"));
}
