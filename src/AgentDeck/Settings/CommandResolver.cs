using System.Diagnostics;
using System.Runtime.InteropServices;
using AgentDeck.Models;
using AgentDeck.Terminal;

namespace AgentDeck.Settings;

/// <summary>
/// Предстартовая проверка команды утилиты. Смысл — не отдавать пользователю
/// «bash: codex: command not found» внутри упавшего терминала, а сказать прямо,
/// что путь нужно поправить в настройках.
/// </summary>
public sealed class CommandResolver
{
    /// <summary>
    /// Символы, по которым команда считается shell-выражением: разбирать её
    /// самостоятельно бессмысленно, пусть этим займётся сам shell.
    /// </summary>
    private const string ShellMetacharacters = "|&;<>$(){}[]*?`\"'\n\r";

    /// <summary>
    /// Таймаут опроса shell — процесс не должен подвешивать нажатие кнопки.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<string, bool> _shellProbe;

    /// <summary>
    /// Создаёт проверку. По умолчанию недостающая команда переспрашивается
    /// у login-shell — тесты подставляют сюда заглушку.
    /// </summary>
    /// <param name="shellProbe">
    /// Запасная проверка: получает имя команды, возвращает true, если shell её видит.
    /// </param>
    public CommandResolver(Func<string, bool>? shellProbe = null)
    {
        _shellProbe = shellProbe ?? ProbeThroughShell;
    }

    /// <summary>
    /// Проверяет, что команду есть чем запускать.
    /// </summary>
    /// <param name="command">
    /// Команда утилиты; пустая означает обычный shell.
    /// </param>
    /// <param name="environment">
    /// Окружение будущего процесса — источник PATH.
    /// </param>
    /// <param name="workingDirectory">
    /// Рабочая директория будущего процесса — относительно неё shell будет
    /// искать команды вида <c>./run.sh</c>.
    /// </param>
    /// <returns>
    /// null, если запуск возможен; иначе имя команды, которую не удалось найти.
    /// </returns>
    public string? FindMissingCommand(
        string? command,
        IReadOnlyDictionary<string, string>? environment = null,
        string? workingDirectory = null)
    {
        var trimmed = (command ?? string.Empty).Trim();

        // Пустая команда — интерактивный shell, проверять нечего.
        if (trimmed.Length == 0 || trimmed.AsSpan().IndexOfAny(ShellMetacharacters) >= 0)
        {
            return null;
        }

        // Команда из одних присваиваний (FOO=bar) ничего не запускает —
        // это законное выражение shell, и проверять в нём нечего.
        if (ShellCommand.ExecutableToken(trimmed) is not { } token)
        {
            return null;
        }

        var expanded = PathUtilities.ExpandHome(token);

        if (expanded.Contains(Path.DirectorySeparatorChar) || expanded.Contains(Path.AltDirectorySeparatorChar))
        {
            return IsExecutableFile(Resolve(expanded, workingDirectory)) ? null : token;
        }

        if (FoundInPath(expanded, environment))
        {
            return null;
        }

        // PATH приложения беднее, чем PATH пользовательского shell: приложение
        // запускают из IDE или лаунчера, где нет ни ~/.local/bin, ни nvm.
        // Переспрашиваем у shell, прежде чем ругаться.
        return _shellProbe(expanded) ? null : token;
    }

    /// <summary>
    /// Приводит относительный путь к рабочей директории будущего процесса:
    /// команду выполнит shell, запущенный в директории тайла, а не в текущей
    /// директории приложения — иначе живой <c>./run.sh</c> объявляется ненайденным.
    /// </summary>
    private static string Resolve(string path, string? workingDirectory)
    {
        if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(workingDirectory))
        {
            return path;
        }

        try
        {
            return Path.Combine(workingDirectory, path);
        }
        catch (ArgumentException)
        {
            // Директория с недопустимыми символами — проверяем путь как есть.
            return path;
        }
    }

    /// <summary>
    /// Ищет команду в каталогах PATH указанного окружения.
    /// </summary>
    private static bool FoundInPath(string token, IReadOnlyDictionary<string, string>? environment)
    {
        var path = environment is not null && environment.TryGetValue("PATH", out var fromEnvironment)
            ? fromEnvironment
            : Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in Candidates(directory.Trim(), token))
            {
                if (IsExecutableFile(candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Возможные имена файла в каталоге: на Windows — с расширениями из PATHEXT.
    /// </summary>
    private static IEnumerable<string> Candidates(string directory, string token)
    {
        if (directory.Length == 0)
        {
            yield break;
        }

        string combined;

        try
        {
            combined = Path.Combine(directory, token);
        }
        catch (ArgumentException)
        {
            // Каталог с недопустимыми символами — просто пропускаем.
            yield break;
        }

        yield return combined;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield break;
        }

        var extensions = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";

        foreach (var extension in extensions.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return combined + extension;
        }
    }

    /// <summary>
    /// Файл существует и его можно исполнить (на Unix — по биту x).
    /// </summary>
    private static bool IsExecutableFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return true;
            }

            const UnixFileMode executable = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & executable) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Спрашивает у shell, знает ли он команду. Флаги те же, что и при запуске
    /// (<see cref="AgentLaunchProfile.UnixShellFlags"/>), иначе проверка и запуск
    /// разойдутся: shell видел бы команду, а тайл писал бы «command not found»
    /// или наоборот.
    /// </summary>
    private static bool ProbeThroughShell(string token)
    {
        var shell = AgentLaunchProfile.ResolveShell();

        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("where");
            startInfo.ArgumentList.Add(token);
        }
        else
        {
            foreach (var flag in AgentLaunchProfile.UnixShellFlags)
            {
                startInfo.ArgumentList.Add(flag);
            }

            startInfo.ArgumentList.Add("command -v -- \"$1\"");
            startInfo.ArgumentList.Add("agentdeck");
            startInfo.ArgumentList.Add(token);
        }

        try
        {
            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);

                // Медленные rc-файлы (nvm, автодополнения) — не повод объявлять
                // команду отсутствующей: пусть попробует запуститься.
                return true;
            }

            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // Не смогли даже спросить — не выдаём ложного «команда не найдена».
            return true;
        }
    }
}
