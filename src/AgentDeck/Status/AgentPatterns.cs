using System.Text.RegularExpressions;
using AgentDeck.Models;

namespace AgentDeck.Status;

/// <summary>
/// Категория сигнала, найденного в выводе агента.
/// </summary>
public enum AgentSignal
{
    /// <summary>
    /// Агент ждёт подтверждения действия.
    /// </summary>
    Permission,

    /// <summary>
    /// Агент работает: крутится индикатор или показан способ прервать.
    /// </summary>
    Busy,
}

/// <summary>
/// Наборы regex для распознавания состояния агента по последним строкам буфера.
/// Паттерны сняты с фактического вывода CLI и изолированы здесь: при смене
/// формата ломается только этот файл, а детектор деградирует до слоя активности.
/// </summary>
public static partial class AgentPatterns
{
    /// <summary>
    /// Начало строки с возможной рамкой диалога и отступом.
    /// Требование «в начале строки» отсекает упоминания фраз внутри текста.
    /// </summary>
    private const string LineStart = @"^[\s│┃║┊╎|>]*";

    private static readonly Regex[] None = [];

    // ── Claude Code (проверено на 2.1.220) ──

    private static readonly Regex[] ClaudePermission =
    [
        // «Do you want to proceed?», «Do you want to allow this connection?»
        Compile($@"{LineStart}Do you want to .+\?\s*$"),

        // «Would you like to:», «Would you like to install it?»
        Compile($@"{LineStart}Would you like to\b.*[?:]\s*$"),

        // Подвал диалога выбора — общий для всех подтверждений Claude Code.
        Compile($@"{LineStart}Enter to confirm\b"),

        // Курсор нумерованного списка: «❯ 1. Yes», «❯ 1. Yes, I trust this folder».
        Compile($@"{LineStart}❯\s*1\.\s*\S"),
    ];

    private static readonly Regex[] ClaudeBusy =
    [
        // Старый индикатор: «✻ Thinking… (esc to interrupt)».
        Compile(@"esc to interrupt", RegexOptions.IgnoreCase),

        // Текущий индикатор: «✻ Scurrying… (1s)» — герундий и счётчик секунд.
        Compile(@"…\s*\(\d+s\)"),
    ];

    // ── Codex CLI (проверено на 0.146.0) ──

    private static readonly Regex[] CodexPermission =
    [
        Compile($@"{LineStart}Allow Codex to run\b"),
        Compile(@"\[y/n\]", RegexOptions.IgnoreCase),
        Compile($@"{LineStart}Continue( anyway)?\?\s*\[", RegexOptions.IgnoreCase),
    ];

    private static readonly Regex[] CodexBusy =
    [
        Compile(@"to interrupt", RegexOptions.IgnoreCase),
    ];

    // ── cursor-agent (проверено на 2026.07.08) ──

    private static readonly Regex[] CursorAgentPermission =
    [
        Compile($@"{LineStart}Run this command\b.*\?\s*$"),
        Compile($@"{LineStart}Run this MCP tool\?\s*$"),
        Compile($@"{LineStart}Allow (this )?web (fetch|search)\?\s*$"),
        Compile($@"{LineStart}Proceed with this edit\?\s*$"),
    ];

    private static readonly Regex[] CursorAgentBusy =
    [
        Compile(@"to interrupt", RegexOptions.IgnoreCase),
    ];

    // ── OpenCode ──
    // CLI недоступен в среде разработки, поэтому набор намеренно консервативный:
    // общие формы вопроса-подтверждения. Ложных срабатываний он не даёт,
    // а пропуск паттерна деградирует до Running/AwaitingInput по активности буфера.

    private static readonly Regex[] OpenCodePermission =
    [
        Compile($@"{LineStart}(Allow|Approve) .+\?\s*$"),
        Compile($@"{LineStart}Do you want to .+\?\s*$"),
        Compile(@"\[y/n\]", RegexOptions.IgnoreCase),
    ];

    private static readonly Regex[] OpenCodeBusy =
    [
        Compile(@"to interrupt", RegexOptions.IgnoreCase),
    ];

    /// <summary>
    /// Возвращает паттерны запроса подтверждения для указанного CLI.
    /// </summary>
    public static IReadOnlyList<Regex> Permission(AgentKind kind) => kind switch
    {
        AgentKind.Claude => ClaudePermission,
        AgentKind.Codex => CodexPermission,
        AgentKind.CursorAgent => CursorAgentPermission,
        AgentKind.OpenCode => OpenCodePermission,
        _ => None,
    };

    /// <summary>
    /// Возвращает паттерны занятости для указанного CLI.
    /// </summary>
    public static IReadOnlyList<Regex> Busy(AgentKind kind) => kind switch
    {
        AgentKind.Claude => ClaudeBusy,
        AgentKind.Codex => CodexBusy,
        AgentKind.CursorAgent => CursorAgentBusy,
        AgentKind.OpenCode => OpenCodeBusy,
        _ => None,
    };

    /// <summary>
    /// Ищет сигнал в строках буфера. Подтверждение приоритетнее занятости:
    /// диалог подтверждения может висеть поверх работающего индикатора.
    /// </summary>
    public static AgentSignal? Match(AgentKind kind, IReadOnlyList<string> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        if (MatchesAny(Permission(kind), rows))
        {
            return AgentSignal.Permission;
        }

        return MatchesAny(Busy(kind), rows) ? AgentSignal.Busy : null;
    }

    private static bool MatchesAny(IReadOnlyList<Regex> patterns, IReadOnlyList<string> rows)
    {
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row))
            {
                continue;
            }

            var trimmed = row.TrimEnd();

            foreach (var pattern in patterns)
            {
                if (pattern.IsMatch(trimmed))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Regex Compile(string pattern, RegexOptions options = RegexOptions.None)
        => new(pattern, options | RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
