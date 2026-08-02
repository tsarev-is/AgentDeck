using AgentDeck.Models;

namespace AgentDeck.Settings;

/// <summary>
/// Сопоставляет утилиту профилю паттернов статуса. Утилиты задаёт пользователь,
/// а наборы regex в <see cref="Status.AgentPatterns"/> привязаны к известным CLI,
/// поэтому профиль выводится из имени утилиты и её команды.
/// </summary>
public static class AgentKindProfile
{
    private static readonly IReadOnlyList<AgentKind> Known =
    [
        AgentKind.Claude,
        AgentKind.Codex,
        AgentKind.OpenCode,
        AgentKind.CursorAgent,
    ];

    /// <summary>
    /// Определяет профиль паттернов: сначала по имени утилиты, затем по имени
    /// исполняемого файла в команде. Незнакомая утилита получает
    /// <see cref="AgentKind.Script"/> — пустой набор паттернов, статус тогда
    /// определяется только по активности буфера и коду возврата.
    /// </summary>
    public static AgentKind Resolve(string? name, string? command)
        => Match(name) ?? Match(ExecutableStem(command)) ?? AgentKind.Script;

    /// <summary>
    /// Возвращает имя исполняемого файла из команды без пути и расширения:
    /// «~/.local/bin/codex --full-auto» → «codex». Ведущие присваивания
    /// окружения пропускаются, иначе «FOO=bar claude» осталось бы без своих
    /// паттернов статуса.
    /// </summary>
    internal static string? ExecutableStem(string? command)
    {
        return ShellCommand.ExecutableToken(command) is { } token
            ? Path.GetFileNameWithoutExtension(token)
            : null;
    }

    private static AgentKind? Match(string? candidate)
    {
        var trimmed = (candidate ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        foreach (var kind in Known)
        {
            if (trimmed.StartsWith(kind.CommandName(), StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return null;
    }
}
