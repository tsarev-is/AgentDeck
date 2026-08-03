using System.Runtime.InteropServices;
using AgentDeck.Models;

namespace AgentDeck.Terminal;

/// <summary>
/// Команда запуска CLI в PTY: исполняемый файл, аргументы, рабочая директория и окружение.
/// </summary>
/// <param name="Kind">
/// Тип запускаемого CLI.
/// </param>
/// <param name="App">
/// Путь к исполняемому файлу.
/// </param>
/// <param name="CommandLine">
/// Аргументы (без argv[0]).
/// </param>
/// <param name="WorkingDirectory">
/// Рабочая директория процесса.
/// </param>
/// <param name="Environment">
/// Переменные окружения процесса.
/// </param>
public sealed record AgentLaunchProfile(
    AgentKind Kind,
    string App,
    IReadOnlyList<string> CommandLine,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>
    /// Shell по умолчанию для Unix, если переменная SHELL не задана.
    /// </summary>
    public const string DefaultUnixShell = "/bin/bash";

    /// <summary>
    /// Значение TERM, объявляемое запускаемому процессу.
    /// </summary>
    public const string TerminalType = "xterm-256color";

    /// <summary>
    /// Флаги запуска команды в Unix-shell: login + интерактивный. Одного
    /// <c>-lc</c> мало — стандартный <c>~/.bashrc</c> начинается с
    /// <c>[[ $- != *i* ]] &amp;&amp; return</c>, поэтому неинтерактивный shell не читает
    /// ни PATH (nvm, ~/.local/bin, cargo), ни алиасы пользователя, и команда,
    /// работающая в его терминале, падает с «command not found».
    /// </summary>
    public static readonly IReadOnlyList<string> UnixShellFlags = ["-l", "-i", "-c"];

    /// <summary>
    /// Строит профиль запуска: команда утилиты идёт через shell пользователя,
    /// чтобы окружение совпадало с его терминалом, пустая команда — это сам shell.
    /// </summary>
    /// <param name="kind">
    /// Профиль паттернов статуса запускаемой утилиты.
    /// </param>
    /// <param name="command">
    /// Команда утилиты; пустая означает интерактивный shell.
    /// </param>
    /// <param name="directory">
    /// Рабочая директория процесса.
    /// </param>
    public static AgentLaunchProfile Create(AgentKind kind, string? command, string directory)
    {
        var environment = BuildEnvironment();
        var trimmed = (command ?? string.Empty).Trim();

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? CreateWindows(kind, trimmed, directory, environment)
            : CreateUnix(kind, trimmed, directory, environment);
    }

    /// <summary>
    /// Возвращает shell пользователя: SHELL на Unix, ComSpec на Windows.
    /// </summary>
    public static string ResolveShell()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var comSpec = System.Environment.GetEnvironmentVariable("ComSpec");
            return string.IsNullOrWhiteSpace(comSpec)
                ? Path.Combine(System.Environment.SystemDirectory, "cmd.exe")
                : comSpec;
        }

        var shell = System.Environment.GetEnvironmentVariable("SHELL");
        return string.IsNullOrWhiteSpace(shell) ? DefaultUnixShell : shell;
    }

    private static AgentLaunchProfile CreateUnix(
        AgentKind kind,
        string command,
        string directory,
        IReadOnlyDictionary<string, string> environment)
    {
        var shell = ResolveShell();

        // Пустая команда — сам shell: в PTY у него есть терминал, интерактивным
        // он становится без флагов. Утилита же стартует login-интерактивным
        // shell'ом, иначе её окружение окажется беднее пользовательского.
        string[] commandLine = command.Length == 0
            ? []
            : [.. UnixShellFlags, command];

        return new AgentLaunchProfile(kind, shell, commandLine, directory, environment);
    }

    private static AgentLaunchProfile CreateWindows(
        AgentKind kind,
        string command,
        string directory,
        IReadOnlyDictionary<string, string> environment)
    {
        if (command.Length == 0)
        {
            var powerShell = Path.Combine(System.Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            return new AgentLaunchProfile(kind, powerShell, [], directory, environment);
        }

        var comSpec = ResolveShell();
        return new AgentLaunchProfile(kind, comSpec, ["/c", command], directory, environment);
    }

    /// <summary>
    /// Копирует окружение процесса и объявляет тип терминала.
    /// </summary>
    private static Dictionary<string, string> BuildEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                environment[key] = value;
            }
        }

        environment["TERM"] = TerminalType;
        environment["COLORTERM"] = "truecolor";

        // Переменные Avalonia/.NET, сбивающие с толку дочерние процессы.
        environment.Remove("DOTNET_STARTUP_HOOKS");

        return environment;
    }
}
