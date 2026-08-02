using AgentDeck.Models;
using AgentDeck.Status;
using NUnit.Framework;

namespace AgentDeck.Tests.Status;

/// <summary>
/// Паттерны статусов на реальных фрагментах вывода CLI-агентов.
/// Фрагменты сняты с установленных версий: Claude Code 2.1.220,
/// Codex CLI 0.146.0, cursor-agent 2026.07.08.
/// </summary>
[TestFixture]
public class AgentPatternsTests
{
    /// <summary>
    /// Диалог подтверждения Claude Code распознаётся как запрос разрешения.
    /// </summary>
    [Test]
    public void Claude_PermissionDialog_IsPermission()
    {
        string[] rows =
        [
            "╭──────────────────────────────────────────────╮",
            "│ Bash command                                 │",
            "│                                              │",
            "│   rm -rf node_modules && npm ci              │",
            "│                                              │",
            "│ Do you want to proceed?                      │",
            "│ ❯ 1. Yes                                     │",
            "│   2. Yes, and don't ask again                │",
            "│   3. No, tell Claude what to do differently  │",
            "╰──────────────────────────────────────────────╯",
        ];

        Assert.That(AgentPatterns.Match(AgentKind.Claude, rows), Is.EqualTo(AgentSignal.Permission));
    }

    /// <summary>
    /// Диалог доверия к папке Claude Code — тоже запрос разрешения.
    /// </summary>
    [Test]
    public void Claude_TrustFolderDialog_IsPermission()
    {
        string[] rows =
        [
            "Quick safety check: Is this a project you created or one you trust?",
            "",
            "❯ 1. Yes, I trust this folder",
            "  2. No, exit",
            "",
            "Enter to confirm · Esc to cancel",
        ];

        Assert.That(AgentPatterns.Match(AgentKind.Claude, rows), Is.EqualTo(AgentSignal.Permission));
    }

    /// <summary>
    /// Прочие формы вопроса Claude Code тоже распознаются.
    /// </summary>
    [TestCase("Do you want to allow this connection?")]
    [TestCase("│ Do you want to allow Claude to fetch this content?")]
    [TestCase("Would you like to install it?")]
    [TestCase("Would you like to:")]
    public void Claude_QuestionForms_AreRecognized(string line)
    {
        Assert.That(AgentPatterns.Match(AgentKind.Claude, [line]), Is.EqualTo(AgentSignal.Permission));
    }

    /// <summary>
    /// Индикатор работы Claude Code распознаётся как занятость.
    /// </summary>
    [TestCase("✻ Scurrying… (1s)")]
    [TestCase("✶ Churning… (42s)")]
    [TestCase("✻ Thinking… (esc to interrupt)")]
    public void Claude_BusyIndicator_IsBusy(string line)
    {
        Assert.That(AgentPatterns.Match(AgentKind.Claude, [line]), Is.EqualTo(AgentSignal.Busy));
    }

    /// <summary>
    /// Спокойный экран Claude Code сигналов не даёт.
    /// </summary>
    [Test]
    public void Claude_IdlePrompt_HasNoSignal()
    {
        string[] rows =
        [
            "  ◉ xhigh · /effort",
            "────────────────────────────────────────────",
            "❯ ",
            "────────────────────────────────────────────",
            "  ⏵⏵ auto mode on (shift+tab to cycle)",
            "     15545 tokens",
        ];

        Assert.That(AgentPatterns.Match(AgentKind.Claude, rows), Is.Null);
    }

    /// <summary>
    /// Near-miss: упоминание фразы внутри строки текста не считается запросом.
    /// </summary>
    [TestCase("The README says: Do you want to proceed? is printed by the installer")]
    [TestCase("grep -n 'Do you want to proceed?' src/main.ts")]
    [TestCase("  42 |   console.log(\"Would you like to continue?\");")]
    public void Claude_PhraseInsideText_IsNotPermission(string line)
    {
        Assert.That(AgentPatterns.Match(AgentKind.Claude, [line]), Is.Null);
    }

    /// <summary>
    /// Обычный промпт ввода Claude Code не путается с курсором выбора «❯ 1.».
    /// </summary>
    [TestCase("❯ ")]
    [TestCase("❯ напиши ровно слово ГОТОВО")]
    public void Claude_InputPrompt_IsNotPermission(string line)
    {
        Assert.That(AgentPatterns.Match(AgentKind.Claude, [line]), Is.Null);
    }

    /// <summary>
    /// Запрос Codex на запуск команды распознаётся.
    /// </summary>
    [Test]
    public void Codex_AllowCommand_IsPermission()
    {
        string[] rows =
        [
            "Allow Codex to run `npm test` in this workspace?",
            "  ❯ Yes  ·  No",
        ];

        Assert.That(AgentPatterns.Match(AgentKind.Codex, rows), Is.EqualTo(AgentSignal.Permission));
    }

    /// <summary>
    /// Классический y/n-вопрос Codex распознаётся.
    /// </summary>
    [TestCase("Continue anyway? [y/N]: ")]
    [TestCase("Continue? [y/N]:")]
    [TestCase("Overwrite file [y/n] ")]
    public void Codex_YesNoPrompt_IsPermission(string line)
    {
        Assert.That(AgentPatterns.Match(AgentKind.Codex, [line]), Is.EqualTo(AgentSignal.Permission));
    }

    /// <summary>
    /// Индикатор работы Codex распознаётся как занятость.
    /// </summary>
    [Test]
    public void Codex_Working_IsBusy()
    {
        Assert.That(
            AgentPatterns.Match(AgentKind.Codex, ["Working (12s • Esc to interrupt)"]),
            Is.EqualTo(AgentSignal.Busy));
    }

    /// <summary>
    /// Запросы cursor-agent распознаются.
    /// </summary>
    [TestCase("Run this command?")]
    [TestCase("Run this command outside the sandbox?")]
    [TestCase("Run this MCP tool?")]
    [TestCase("Allow this web fetch?")]
    [TestCase("Allow web search?")]
    [TestCase("Proceed with this edit?")]
    public void CursorAgent_Prompts_ArePermission(string line)
    {
        Assert.That(AgentPatterns.Match(AgentKind.CursorAgent, [line]), Is.EqualTo(AgentSignal.Permission));
    }

    /// <summary>
    /// Near-miss cursor-agent: та же фраза внутри текста не считается запросом.
    /// </summary>
    [Test]
    public void CursorAgent_PhraseInsideText_IsNotPermission()
    {
        Assert.That(
            AgentPatterns.Match(AgentKind.CursorAgent, ["The prompt \"Run this command?\" appears in the docs"]),
            Is.Null);
    }

    /// <summary>
    /// Общие формы подтверждения OpenCode распознаются.
    /// </summary>
    [TestCase("Allow this command?")]
    [TestCase("Approve the following edit?")]
    [TestCase("Do you want to apply this patch?")]
    [TestCase("Apply changes? [y/n]")]
    public void OpenCode_GenericPrompts_ArePermission(string line)
    {
        Assert.That(AgentPatterns.Match(AgentKind.OpenCode, [line]), Is.EqualTo(AgentSignal.Permission));
    }

    /// <summary>
    /// У «script» паттернов нет: статус определяется кодом возврата и активностью.
    /// </summary>
    [TestCase("Do you want to proceed?")]
    [TestCase("Allow Codex to run `ls`")]
    [TestCase("✻ Scurrying… (3s)")]
    [TestCase("Continue? [y/N]")]
    public void Script_HasNoPatterns(string line)
    {
        Assert.Multiple(() =>
        {
            Assert.That(AgentPatterns.Match(AgentKind.Script, [line]), Is.Null);
            Assert.That(AgentPatterns.Permission(AgentKind.Script), Is.Empty);
            Assert.That(AgentPatterns.Busy(AgentKind.Script), Is.Empty);
        });
    }

    /// <summary>
    /// Подтверждение приоритетнее занятости: диалог висит поверх индикатора.
    /// </summary>
    [Test]
    public void Permission_WinsOverBusy()
    {
        string[] rows =
        [
            "✻ Scurrying… (7s)",
            "│ Do you want to proceed?",
        ];

        Assert.That(AgentPatterns.Match(AgentKind.Claude, rows), Is.EqualTo(AgentSignal.Permission));
    }

    /// <summary>
    /// Пустой и полностью пробельный буфер сигналов не даёт.
    /// </summary>
    [Test]
    public void EmptyBuffer_HasNoSignal()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AgentPatterns.Match(AgentKind.Claude, []), Is.Null);
            Assert.That(AgentPatterns.Match(AgentKind.Claude, ["", "   ", "\t"]), Is.Null);
        });
    }
}
