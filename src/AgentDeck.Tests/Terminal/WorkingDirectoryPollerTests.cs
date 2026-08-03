using AgentDeck.Status;
using AgentDeck.Terminal;
using AgentDeck.ViewModels;
using NUnit.Framework;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Опрашиватель рабочих директорий: «cd» внутри терминала доезжает до
/// заголовка тайла, а мигание команд переднего плана — нет.
/// </summary>
[TestFixture]
public class WorkingDirectoryPollerTests
{
    /// <summary>
    /// Первый ответ процесса — точка отсчёта: путь от ядра приходит без
    /// символических ссылок и может не совпасть с тем, которым тайл запускали.
    /// </summary>
    [Test]
    public void FirstProbe_KeepsTitle()
    {
        var deck = CreateDeck("~/work/api", out var tile);
        var poller = CreatePoller(deck, () => "/mnt/data/work/api");

        poller.Tick();
        poller.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(tile.Directory, Is.EqualTo("~/work/api"));
            Assert.That(tile.Title, Is.EqualTo("api"));
        });
    }

    /// <summary>
    /// Устоявшийся новый путь переносится в тайл вместе с заголовком.
    /// </summary>
    [Test]
    public void ChangedDirectory_MovesToTile()
    {
        var deck = CreateDeck("/work/api", out var tile);
        var directory = "/work/api";
        var poller = CreatePoller(deck, () => directory);
        var changes = 0;
        poller.DirectoryChanged += (_, _) => changes++;

        poller.Tick();
        directory = "/work/billing";
        poller.Tick();
        poller.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(tile.Directory, Is.EqualTo("/work/billing"));
            Assert.That(tile.Title, Is.EqualTo("billing"));
            Assert.That(changes, Is.EqualTo(1), "сессию переписываем один раз на один «cd»");
        });
    }

    /// <summary>
    /// Путь, увиденный один тик, заголовок не трогает: команда переднего плана
    /// могла уйти в свой каталог на доли секунды.
    /// </summary>
    [Test]
    public void SingleTickDirectory_DoesNotMoveTile()
    {
        var deck = CreateDeck("/work/api", out var tile);
        var directory = "/work/api";
        var poller = CreatePoller(deck, () => directory);

        poller.Tick();
        directory = "/tmp/build";
        poller.Tick();
        directory = "/work/api";
        poller.Tick();
        poller.Tick();

        Assert.That(tile.Directory, Is.EqualTo("/work/api"));
    }

    /// <summary>
    /// Домашняя директория в заголовке остаётся короткой — как и введённая руками.
    /// </summary>
    [Test]
    public void HomeDirectory_CollapsesToTilde()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var deck = CreateDeck("/work/api", out var tile);
        var directory = "/work/api";
        var poller = CreatePoller(deck, () => directory);

        poller.Tick();
        directory = Path.Combine(home, "notes");
        poller.Tick();
        poller.Tick();

        Assert.That(tile.Directory, Is.EqualTo("~/notes"));
    }

    /// <summary>
    /// Плейсхолдер не опрашивается: путь там набирает пользователь, и подменять
    /// его на полпути нельзя.
    /// </summary>
    [Test]
    public void Placeholder_IsLeftAlone()
    {
        var deck = CreateDeck("/work/api", out var tile);
        tile.Status = TileStatus.Placeholder;
        var poller = CreatePoller(deck, () => "/work/billing");

        poller.Tick();
        poller.Tick();
        poller.Tick();

        Assert.That(tile.Directory, Is.EqualTo("/work/api"));
    }

    /// <summary>
    /// Умерший процесс стирает точку отсчёта: перезапущенный тайл начинает
    /// отсчёт заново со своей директории.
    /// </summary>
    [Test]
    public void DeadProcess_ForgetsBaseline()
    {
        var deck = CreateDeck("/work/api", out var tile);
        string? directory = "/work/api";
        var poller = CreatePoller(deck, () => directory);

        poller.Tick();
        directory = null;
        poller.Tick();

        // Перезапуск в другой директории: это старт процесса, а не «cd».
        directory = "/work/billing";
        poller.Tick();
        poller.Tick();

        Assert.That(tile.Directory, Is.EqualTo("/work/api"));
    }

    /// <summary>
    /// Сброс тайла забывает точку отсчёта так же, как смерть процесса.
    /// </summary>
    [Test]
    public void Reset_ForgetsBaseline()
    {
        var deck = CreateDeck("/work/api", out var tile);
        var directory = "/work/api";
        var poller = CreatePoller(deck, () => directory);

        poller.Tick();
        poller.Reset(tile.Id);
        directory = "/work/billing";
        poller.Tick();
        poller.Tick();

        Assert.That(tile.Directory, Is.EqualTo("/work/api"));
    }

    /// <summary>
    /// Дек с одним запущенным тайлом.
    /// </summary>
    /// <param name="directory">
    /// Директория, с которой тайл запущен.
    /// </param>
    /// <param name="tile">
    /// Созданный тайл.
    /// </param>
    private static DeckViewModel CreateDeck(string directory, out TileViewModel tile)
    {
        var deck = new DeckViewModel();
        tile = deck.AddTile(directory, null)!;
        tile.Status = TileStatus.Running;
        return deck;
    }

    /// <summary>
    /// Опрашиватель без таймера, отдающий тайлам путь от указанного источника.
    /// </summary>
    private static WorkingDirectoryPoller CreatePoller(DeckViewModel deck, Func<string?> probe)
        => new(deck, probe: _ => probe(), useTimer: false);
}
