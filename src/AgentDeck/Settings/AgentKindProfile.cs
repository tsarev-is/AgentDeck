using AgentDeck.Models;

namespace AgentDeck.Settings;

/// <summary>
/// Сопоставляет утилиту профилю паттернов статуса. Утилиты задаёт пользователь,
/// а наборы regex в <see cref="Status.AgentPatterns"/> привязаны к известным CLI,
/// поэтому профиль выводится из имени утилиты и её команды.
/// </summary>
public static class AgentKindProfile
{
    /// <summary>
    /// Формы, под которыми известный CLI попадает в настройки: собственное имя
    /// команды и то, как его зовут на практике. cursor-agent ставит рядом с собой
    /// симлинк «agent», а кнопку с ним обычно называют «cursor»; без этих форм
    /// тайл считается обычным терминалом и работу модели не показывает.
    /// </summary>
    private static readonly (AgentKind Kind, string[] Names)[] Known =
    [
        (AgentKind.Claude, ["claude"]),
        (AgentKind.Codex, ["codex"]),
        (AgentKind.OpenCode, ["opencode"]),
        (AgentKind.CursorAgent, ["cursor-agent", "cursoragent", "cursor", "agent"]),
    ];

    /// <summary>
    /// Определяет профиль паттернов: сначала по имени утилиты, затем по командам,
    /// которые она запускает. Незнакомая утилита получает
    /// <see cref="AgentKind.Script"/> — пустой набор паттернов, и статус тайла
    /// сводится к «процесс жив» и коду возврата.
    /// </summary>
    public static AgentKind Resolve(string? name, string? command)
        => Match(name) ?? MatchCommand(command) ?? AgentKind.Script;

    /// <summary>
    /// Ищет знакомый CLI среди команд, которые запускает строка.
    /// </summary>
    /// <param name="command">
    /// Команда утилиты как её ввёл пользователь.
    /// </param>
    /// <returns>
    /// null, если знакомого CLI в команде нет.
    /// </returns>
    /// <remarks>
    /// Смотреть на все слова команды нельзя: <c>ssh agent</c> запускает ssh, а
    /// <c>tail -f agentd.log</c> — tail, и приняв их за cursor-agent, тайл
    /// получил бы чужие паттерны — а вместе с ними и состояния, которых у
    /// терминала нет: вместо «процесс жив» лампочка объявляла бы «ход за
    /// пользователем». Поэтому кандидаты берутся только с позиций команд —
    /// подробности в <see cref="ShellCommand.ExecutableTokens"/>.
    /// </remarks>
    internal static AgentKind? MatchCommand(string? command)
    {
        foreach (var token in ShellCommand.ExecutableTokens(command))
        {
            if (Match(ShellCommand.Stem(token)) is { } kind)
            {
                return kind;
            }
        }

        return null;
    }

    private static AgentKind? Match(string? candidate)
    {
        var trimmed = (candidate ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        foreach (var (kind, names) in Known)
        {
            foreach (var name in names)
            {
                if (IsNamed(trimmed, name))
                {
                    return kind;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Кандидат назван именем известного CLI — сам по себе или с продолжением
    /// через разделитель.
    /// </summary>
    /// <param name="candidate">
    /// Имя утилиты или имя её исполняемого файла.
    /// </param>
    /// <param name="name">
    /// Одна из форм известного CLI.
    /// </param>
    /// <remarks>
    /// Продолжение бывает и у имени кнопки («claude sonnet»), и у файла
    /// («claude-code» из npm-пакета), поэтому одного равенства мало. Но
    /// продолжение через букву или цифру — это уже другое слово: «agentdeck» и
    /// «cursorless» к cursor-agent отношения не имеют.
    /// </remarks>
    private static bool IsNamed(string candidate, string name)
        => candidate.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && (candidate.Length == name.Length || !char.IsLetterOrDigit(candidate[name.Length]));
}
