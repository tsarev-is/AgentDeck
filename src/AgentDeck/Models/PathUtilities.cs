namespace AgentDeck.Models;

/// <summary>
/// Общие операции над путями, которые нужны и плейсхолдеру, и проверке команд.
/// </summary>
public static class PathUtilities
{
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
}
