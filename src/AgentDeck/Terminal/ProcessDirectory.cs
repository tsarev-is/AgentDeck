namespace AgentDeck.Terminal;

/// <summary>
/// Рабочая директория живого процесса по данным «/proc». Другого следа «cd»
/// внутри терминала не оставляет: в PTY уходит только вывод команды, поэтому
/// узнать новый путь можно единственным способом — спросив ядро.
/// </summary>
public static class ProcessDirectory
{
    /// <summary>
    /// Позиция tpgid среди полей «/proc/&lt;pid&gt;/stat», считая от первого поля
    /// после имени процесса: state, ppid, pgrp, session, tty_nr, tpgid.
    /// </summary>
    private const int TpgidIndex = 5;

    /// <summary>
    /// Пометка, которой ядро снабжает цель «cwd», когда каталог удалили из-под
    /// живого процесса.
    /// </summary>
    private const string DeletedMarker = " (deleted)";

    /// <summary>
    /// Возвращает рабочую директорию процесса, который прямо сейчас держит
    /// терминал: у простого shell'а это он сам, а у запущенной из него команды
    /// (вложенный shell, ssh, mc) — она.
    /// </summary>
    /// <param name="pid">
    /// Идентификатор процесса, поднятого в PTY.
    /// </param>
    /// <returns>
    /// null, если спросить не у кого: процесс уже умер, прав на него нет или
    /// система не Linux.
    /// </returns>
    public static string? ReadForeground(int pid)
    {
        // Группа переднего плана — это отдельный процесс, и умереть он может
        // между двумя чтениями: тогда откатываемся к самому владельцу PTY.
        var foreground = ReadForegroundGroup(pid);

        return (foreground is { } group ? Read(group) : null) ?? Read(pid);
    }

    /// <summary>
    /// Читает рабочую директорию процесса.
    /// </summary>
    /// <param name="pid">
    /// Идентификатор процесса.
    /// </param>
    /// <returns>
    /// null, если директорию прочитать не удалось или каталога процесса больше
    /// нет.
    /// </returns>
    public static string? Read(int pid)
    {
        if (!OperatingSystem.IsLinux() || pid <= 0)
        {
            return null;
        }

        try
        {
            // «/proc/<pid>/cwd» — символическая ссылка на каталог, и LinkTarget
            // отдаёт её цель одним readlink. На умершем или чужом процессе
            // получаем null, а не исключение.
            var target = new FileInfo($"/proc/{pid}/cwd").LinkTarget;

            // Каталог удалили из-под процесса: ядро отдаёт прежний путь с
            // пометкой, и путём эта строка быть перестала. Отдать её тайлу
            // нельзя — она попала бы и в заголовок, и в сессию, из которой тайл
            // потом восстановился бы в каталоге, которого нет.
            return string.IsNullOrEmpty(target) || target.EndsWith(DeletedMarker, StringComparison.Ordinal)
                ? null
                : target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Возвращает группу процессов переднего плана терминала, которым владеет
    /// указанный процесс.
    /// </summary>
    /// <param name="pid">
    /// Идентификатор процесса, поднятого в PTY.
    /// </param>
    /// <returns>
    /// null, если группы нет или её не удалось прочитать.
    /// </returns>
    public static int? ReadForegroundGroup(int pid)
    {
        if (!OperatingSystem.IsLinux() || pid <= 0)
        {
            return null;
        }

        try
        {
            return ParseForegroundGroup(File.ReadAllText($"/proc/{pid}/stat"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Достаёт tpgid — идентификатор группы процессов переднего плана — из
    /// строки «/proc/&lt;pid&gt;/stat».
    /// </summary>
    /// <param name="stat">
    /// Содержимое файла stat.
    /// </param>
    /// <returns>
    /// null для строки без tpgid и для процесса без управляющего терминала,
    /// у которого поле равно -1.
    /// </returns>
    public static int? ParseForegroundGroup(string? stat)
    {
        // Имя процесса — второе поле, и оно приходит в скобках, потому что
        // внутри бывают и пробелы, и сами скобки. Разбор по полям имеет смысл
        // только за последней закрывающей скобкой строки.
        var close = stat?.LastIndexOf(')') ?? -1;

        if (close < 0)
        {
            return null;
        }

        var fields = stat![(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return fields.Length > TpgidIndex && int.TryParse(fields[TpgidIndex], out var tpgid) && tpgid > 0
            ? tpgid
            : null;
    }
}
