namespace AgentDeck.Models;

/// <summary>
/// Общие операции над путями, которые нужны и плейсхолдеру, и проверке команд.
/// </summary>
public static class PathUtilities
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>
    /// Сравнение путей: Windows не различает регистр, Unix различает.
    /// </summary>
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Раскрывает «~» в домашнюю директорию и убирает лишние пробелы.
    /// </summary>
    public static string ExpandHome(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        if (trimmed == "~" || trimmed.StartsWith("~/", StringComparison.Ordinal) || trimmed.StartsWith(@"~\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return trimmed.Length == 1 ? home : Path.Combine(home, trimmed[2..]);
        }

        return trimmed;
    }

    /// <summary>
    /// Свёртывает домашнюю директорию обратно в «~»: путь в заголовке тайла и в
    /// поле выбора папки должен оставаться коротким и читаемым.
    /// </summary>
    public static string CollapseHome(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (trimmed.Length == 0 || home.Length == 0 || !trimmed.StartsWith(home, PathComparison))
        {
            return trimmed;
        }

        if (trimmed.Length == home.Length)
        {
            return "~";
        }

        // «/home/user2» не лежит внутри «/home/user»: за префиксом обязан идти разделитель.
        return Separators.Contains(trimmed[home.Length]) ? string.Concat("~", trimmed[home.Length..]) : trimmed;
    }

    /// <summary>
    /// Родительская директория в том же виде, что и исходный путь, или null,
    /// если подниматься некуда — путь уже корень.
    /// </summary>
    public static string? Parent(string? path)
    {
        // Хвостовой разделитель («/home/user/») иначе дал бы тот же каталог обратно.
        var trimmed = TrimTrailingSeparators(ExpandHome(path));

        if (trimmed.Length == 0)
        {
            return null;
        }

        var parent = Path.GetDirectoryName(trimmed);

        // Пустая строка — относительный путь из одного сегмента: подниматься некуда.
        return string.IsNullOrEmpty(parent) ? null : CollapseHome(parent);
    }

    /// <summary>
    /// Дописывает к пути вложенную директорию, сохраняя вид пути: «~» не
    /// раскрывается, а разделитель берётся тот, что уже используется в пути.
    /// </summary>
    /// <param name="path">
    /// Путь-основа; пришёл из поля ввода, поэтому лишние пробелы с краёв убираем.
    /// </param>
    /// <param name="name">
    /// Имя вложенной директории от файловой системы — дописывается как есть.
    /// Пробелы по краям имени законны на Unix (и ведущие — на Windows), так что
    /// обрезка увела бы путь в чужой или несуществующий каталог.
    /// </param>
    public static string Child(string? path, string? name)
    {
        var directory = (path ?? string.Empty).Trim();
        var folder = name ?? string.Empty;

        if (folder.Length == 0)
        {
            return directory;
        }

        if (directory.Length == 0)
        {
            return folder;
        }

        var separator = !directory.Contains('/') && directory.Contains('\\') ? '\\' : '/';
        var trimmed = TrimTrailingSeparators(directory);

        // Корень («/», «C:\») после обрезки хвоста теряет разделитель — возвращаем его.
        return trimmed.Length == 0 ? $"{separator}{folder}" : $"{trimmed}{separator}{folder}";
    }

    private static string TrimTrailingSeparators(string path) => path.TrimEnd(Separators);
}
