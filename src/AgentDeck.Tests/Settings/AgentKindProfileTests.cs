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
    /// Незнакомая утилита получает пустой профиль: статус сводится к «процесс
    /// жив» и коду возврата, а не к чужим паттернам.
    /// </summary>
    [TestCase("htop", "htop")]
    [TestCase("build", "./run.sh --full")]
    [TestCase("terminal", "bash")]
    [TestCase("SSH-PI4", "ssh -p 29138 root@203.0.113.10")]
    [TestCase("", "")]
    [TestCase(null, null)]
    public void Resolve_UnknownUtility_ReturnsScript(string? name, string? command)
    {
        Assert.That(AgentKindProfile.Resolve(name, command), Is.EqualTo(AgentKind.Script));
    }

    /// <summary>
    /// cursor-agent зовут не своим полным именем: рядом с ним ставится симлинк
    /// «agent», а кнопку называют «cursor». Не узнав его, тайл считался бы
    /// терминалом — лампочка горела бы ровно и работу модели не показывала.
    /// </summary>
    [TestCase("cursor", "agent")]
    [TestCase("cursor", "cursor-agent")]
    [TestCase("agent", "agent --resume")]
    [TestCase("my cli", "~/.local/bin/agent")]
    [TestCase("cursoragent", "")]
    public void Resolve_CursorAgentAliases_ReturnCursorAgent(string name, string command)
    {
        Assert.That(AgentKindProfile.Resolve(name, command), Is.EqualTo(AgentKind.CursorAgent));
    }

    /// <summary>
    /// Агент в цепочке команд узнаётся, хотя первым словом стоит не он: так
    /// выглядит запуск в свежесозданной папке или через обёртку.
    /// </summary>
    [TestCase("research", "dir=\"$HOME/research/$(date +%y%m%d)\" && mkdir -p \"$dir\" && cd \"$dir\" && codex", AgentKind.Codex)]
    [TestCase("start", "npx claude", AgentKind.Claude)]
    [TestCase("root", "sudo -u dev codex --full-auto", AgentKind.Codex)]
    [TestCase("remote", "cd /srv/app && opencode", AgentKind.OpenCode)]
    public void Resolve_AgentInsideCommandChain_IsRecognized(string name, string command, AgentKind expected)
    {
        Assert.That(AgentKindProfile.Resolve(name, command), Is.EqualTo(expected));
    }

    /// <summary>
    /// Имя агента в аргументах чужой команды утилитой не делает: запускается-то
    /// не он. Иначе обычный терминал получил бы паттерны агента, а с ними и
    /// состояния, которых у него нет: лампочка объявляла бы «ход за
    /// пользователем» вместо «процесс жив» и мигала бы на любой сборке, которая
    /// пишет «Working».
    /// </summary>
    [TestCase("remote", "ssh agent")]
    [TestCase("echo", "bash -c \"echo claude\"")]
    [TestCase("logs", "tail -f /var/log/agentd.log")]
    [TestCase("tests", "dotnet test --filter Agent")]
    [TestCase("edit", "nvim AGENTS.md")]
    [TestCase("pod", "kubectl logs -f agentdeck-0")]
    [TestCase("rules", "cd /srv/cursor-rules && bash")]
    [TestCase("build", "cd ~/AgentDeck/src && dotnet run")]
    public void Resolve_AgentNamedInArguments_ReturnsScript(string name, string command)
    {
        Assert.That(AgentKindProfile.Resolve(name, command), Is.EqualTo(AgentKind.Script));
    }

    /// <summary>
    /// Имя известного CLI продолжается через разделитель — это всё ещё он:
    /// «claude-code» из npm-пакета, «codex» с суффиксом сборки. А продолжение
    /// через букву — уже другая программа.
    /// </summary>
    [TestCase("start", "npx @anthropic-ai/claude-code", AgentKind.Claude)]
    [TestCase("cursor 2", "", AgentKind.CursorAgent)]
    [TestCase("agentdeck", "bash", AgentKind.Script)]
    [TestCase("watch", "~/.local/bin/agentd --serve", AgentKind.Script)]
    [TestCase("cursorless", "cursorless", AgentKind.Script)]
    public void Resolve_NameContinuation_NeedsSeparator(string name, string command, AgentKind expected)
    {
        Assert.That(AgentKindProfile.Resolve(name, command), Is.EqualTo(expected));
    }

    /// <summary>
    /// Присваивание в кавычках не сбивает поиск: команда стоит за ним, а не
    /// внутри него.
    /// </summary>
    [Test]
    public void Resolve_QuotedAssignment_FindsCommandAfterIt()
    {
        Assert.That(
            AgentKindProfile.Resolve("my cli", "CLAUDE_CONFIG_DIR=\"$HOME/.claude-team\" DISABLE_TELEMETRY=1 claude --resume"),
            Is.EqualTo(AgentKind.Claude));
    }

    /// <summary>
    /// Настройки пользователя целиком: имена кнопок переименованы, команды —
    /// с окружением, симлинками и цепочками. Каждая обязана получить свой профиль.
    /// </summary>
    [TestCase("terminal", "bash", AgentKind.Script)]
    [TestCase("claude", "claude", AgentKind.Claude)]
    [TestCase("cursor", "agent", AgentKind.CursorAgent)]
    [TestCase("codex", "codex", AgentKind.Codex)]
    public void Resolve_RealWorldSettings_KeepStatusProfiles(string name, string command, AgentKind expected)
    {
        Assert.That(AgentKindProfile.Resolve(name, command), Is.EqualTo(expected));
    }
}
