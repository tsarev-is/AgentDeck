namespace AgentDeck.Models;

/// <summary>
/// Тип запускаемого в тайле CLI.
/// </summary>
public enum AgentKind
{
    /// <summary>
    /// Claude Code.
    /// </summary>
    Claude,

    /// <summary>
    /// Codex CLI.
    /// </summary>
    Codex,

    /// <summary>
    /// OpenCode.
    /// </summary>
    OpenCode,

    /// <summary>
    /// cursor-agent.
    /// </summary>
    CursorAgent,

    /// <summary>
    /// Обычный shell для скриптов, ssh и прочих инструментов.
    /// </summary>
    Script,
}

/// <summary>
/// Отображаемые подписи типов CLI.
/// </summary>
public static class AgentKindNames
{
    /// <summary>
    /// Все типы в порядке отображения на плейсхолдере.
    /// </summary>
    public static readonly IReadOnlyList<AgentKind> All =
    [
        AgentKind.Claude,
        AgentKind.Codex,
        AgentKind.OpenCode,
        AgentKind.CursorAgent,
        AgentKind.Script,
    ];

    /// <summary>
    /// Возвращает имя команды, совпадающее с подписью кнопки запуска.
    /// </summary>
    public static string CommandName(this AgentKind kind) => kind switch
    {
        AgentKind.Claude => "claude",
        AgentKind.Codex => "codex",
        AgentKind.OpenCode => "opencode",
        AgentKind.CursorAgent => "cursor-agent",
        AgentKind.Script => "script",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
