using AgentDeck.Models;
using AgentDeck.Settings;
using NUnit.Framework;

namespace AgentDeck.Tests.Settings;

/// <summary>
/// Вывод профиля паттернов статуса из имени утилиты и её команды.
/// </summary>
[TestFixture]
public class AgentKindProfileTests
{
    /// <summary>
    /// Штатные имена утилит дают свои профили паттернов.
    /// </summary>
    [TestCase("claude", AgentKind.Claude)]
    [TestCase("codex", AgentKind.Codex)]
    [TestCase("opencode", AgentKind.OpenCode)]
    [TestCase("cursor-agent", AgentKind.CursorAgent)]
    public void Resolve_KnownName_ReturnsMatchingKind(string name, AgentKind expected)
    {
        Assert.That(AgentKindProfile.Resolve(name, string.Empty), Is.EqualTo(expected));
    }

    /// <summary>
    /// Регистр имени не важен.
    /// </summary>
    [Test]
    public void Resolve_NameCase_IsIgnored()
    {
        Assert.That(AgentKindProfile.Resolve("Codex", null), Is.EqualTo(AgentKind.Codex));
    }

    /// <summary>
    /// Утилиту, переименованную пользователем, узнаём по исполняемому файлу
    /// в команде — с путём, аргументами и расширением.
    /// </summary>
    [TestCase("~/.local/bin/codex --full-auto", AgentKind.Codex)]
    [TestCase("/usr/local/bin/claude", AgentKind.Claude)]
    [TestCase("cursor-agent.exe", AgentKind.CursorAgent)]
    [TestCase("opencode", AgentKind.OpenCode)]
    public void Resolve_UnknownName_FallsBackToCommand(string command, AgentKind expected)
    {
        Assert.That(AgentKindProfile.Resolve("my agent", command), Is.EqualTo(expected));
    }

    /// <summary>
    /// Присваивания окружения перед командой не мешают узнать утилиту:
    /// иначе переименованный агент остался бы без своих паттернов статуса.
    /// </summary>
    [TestCase("CLAUDE_CONFIG_DIR=/home/user/.claude-team claude --resume", AgentKind.Claude)]
    [TestCase("RUST_LOG=debug LANG=C ~/.local/bin/codex", AgentKind.Codex)]
    public void Resolve_LeadingAssignments_AreSkipped(string command, AgentKind expected)
    {
        Assert.That(AgentKindProfile.Resolve("my agent", command), Is.EqualTo(expected));
    }

    /// <summary>
    /// Имя приоритетнее команды: «npx claude» остаётся Claude.
    /// </summary>
    [Test]
    public void Resolve_NameWins_OverCommand()
    {
        Assert.That(AgentKindProfile.Resolve("claude", "npx claude"), Is.EqualTo(AgentKind.Claude));
    }

    /// <summary>
    /// Незнакомая утилита получает пустой профиль: статус определяется
    /// активностью буфера и кодом возврата, а не чужими паттернами.
    /// </summary>
    [TestCase("htop", "htop")]
    [TestCase("build", "./run.sh --full")]
    [TestCase("", "")]
    [TestCase(null, null)]
    public void Resolve_UnknownUtility_ReturnsScript(string? name, string? command)
    {
        Assert.That(AgentKindProfile.Resolve(name, command), Is.EqualTo(AgentKind.Script));
    }
}
