using System.Runtime.InteropServices;
using AgentDeck.Models;
using AgentDeck.Terminal;
using NUnit.Framework;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Профили запуска CLI на текущей ОС.
/// </summary>
[TestFixture]
public class AgentLaunchProfileTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Директория из плейсхолдера попадает в рабочую директорию процесса.
    /// </summary>
    [Test]
    public void Create_PutsDirectoryIntoWorkingDirectory([Values] AgentKind kind)
    {
        var directory = IsWindows ? @"C:\work\project" : "/home/user/dev/project";

        Assert.That(AgentLaunchProfile.Create(kind, kind.CommandName(), directory).WorkingDirectory, Is.EqualTo(directory));
    }

    /// <summary>
    /// Профиль сохраняет запрошенный профиль паттернов.
    /// </summary>
    [Test]
    public void Create_KeepsKind([Values] AgentKind kind)
    {
        Assert.That(AgentLaunchProfile.Create(kind, kind.CommandName(), "/tmp").Kind, Is.EqualTo(kind));
    }

    /// <summary>
    /// Пустая команда запускает сам shell без аргументов — интерактивная сессия.
    /// </summary>
    [Test]
    public void Create_EmptyCommand_LaunchesShellWithoutArguments([Values("", "   ", null)] string? command)
    {
        var profile = AgentLaunchProfile.Create(AgentKind.Script, command, "/tmp");

        Assert.Multiple(() =>
        {
            Assert.That(profile.CommandLine, Is.Empty);
            Assert.That(profile.App, Is.Not.Empty);
        });

        if (IsWindows)
        {
            Assert.That(profile.App, Does.EndWith("powershell.exe"));
        }
        else
        {
            Assert.That(profile.App, Is.EqualTo(AgentLaunchProfile.ResolveShell()));
            Assert.That(File.Exists(profile.App), Is.True, $"shell {profile.App} должен существовать");
        }
    }

    /// <summary>
    /// Утилиты стартуют через shell, чтобы PATH совпадал с пользовательским.
    /// Команда уходит одним аргументом: путь с «~», флаги и shell-синтаксис
    /// разбирает сам shell.
    /// </summary>
    [TestCase(AgentKind.Claude, "claude")]
    [TestCase(AgentKind.Codex, "codex")]
    [TestCase(AgentKind.OpenCode, "opencode")]
    [TestCase(AgentKind.CursorAgent, "cursor-agent")]
    [TestCase(AgentKind.Codex, "~/.local/bin/codex --full-auto")]
    [TestCase(AgentKind.Script, "./run.sh --full")]
    public void Create_Agent_RunsCommandThroughShell(AgentKind kind, string command)
    {
        var profile = AgentLaunchProfile.Create(kind, command, "/tmp");

        if (IsWindows)
        {
            Assert.That(profile.CommandLine, Is.EqualTo(new[] { "/c", command }));
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(profile.App, Is.EqualTo(AgentLaunchProfile.ResolveShell()));
                Assert.That(
                    profile.CommandLine,
                    Is.EqualTo(new[] { "-l", "-i", "-c", command }),
                    "нужен login-интерактивный shell: без -i не читается ~/.bashrc с PATH и алиасами");
            });
        }
    }

    /// <summary>
    /// Флаги проверки команды и флаги запуска — один и тот же список: если они
    /// разойдутся, проверка начнёт врать про «command not found».
    /// </summary>
    [Test]
    [Platform("Linux,MacOsX")]
    public void UnixShellFlags_EndWithCommandFlag()
    {
        Assert.That(AgentLaunchProfile.UnixShellFlags, Is.EqualTo(new[] { "-l", "-i", "-c" }));
    }

    /// <summary>
    /// Окружение наследуется от приложения и объявляет цветной терминал.
    /// </summary>
    [Test]
    public void Create_DeclaresColorTerminalAndInheritsEnvironment()
    {
        var profile = AgentLaunchProfile.Create(AgentKind.Script, string.Empty, "/tmp");

        Assert.Multiple(() =>
        {
            Assert.That(profile.Environment["TERM"], Is.EqualTo(AgentLaunchProfile.TerminalType));
            Assert.That(profile.Environment["COLORTERM"], Is.EqualTo("truecolor"));
            Assert.That(profile.Environment, Is.Not.Empty);
        });

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            Assert.That(profile.Environment.ContainsKey("PATH"), Is.True, "PATH должен наследоваться");
        }
    }

    /// <summary>
    /// Shell по умолчанию на Unix существует и абсолютен.
    /// </summary>
    [Test]
    [Platform("Linux,MacOsX")]
    public void ResolveShell_OnUnix_ReturnsExistingAbsolutePath()
    {
        var shell = AgentLaunchProfile.ResolveShell();

        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathRooted(shell), Is.True);
            Assert.That(File.Exists(shell), Is.True);
        });
    }
}
