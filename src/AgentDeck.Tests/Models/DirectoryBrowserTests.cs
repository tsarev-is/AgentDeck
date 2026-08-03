using AgentDeck.Models;
using NUnit.Framework;

namespace AgentDeck.Tests.Models;

/// <summary>
/// Чтение вложенных директорий: только каталоги, алфавитный порядок и
/// недоступные пути.
/// </summary>
[TestFixture]
public class DirectoryBrowserTests
{
    private string _root = null!;
    private DirectoryBrowser _browser = null!;

    [SetUp]
    public void SetUp()
    {
        _browser = new DirectoryBrowser();
        _root = Path.Combine(Path.GetTempPath(), $"agentdeck-browse-{Guid.NewGuid():N}");

        foreach (var name in new[] { "billing", "Api", "web", ".git" })
        {
            Directory.CreateDirectory(Path.Combine(_root, name));
        }

        File.WriteAllText(Path.Combine(_root, "notes.txt"), "не каталог");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Возвращаются каталоги, отсортированные без учёта регистра; файлы и
    /// скрытые каталоги в выдачу не попадают.
    /// </summary>
    [Test]
    public void List_ReturnsSortedVisibleDirectoriesOnly()
    {
        var listing = _browser.List(_root);

        Assert.Multiple(() =>
        {
            Assert.That(listing.Exists, Is.True);
            Assert.That(listing.Folders, Is.EqualTo(new[] { "Api", "billing", "web" }));
        });
    }

    /// <summary>
    /// В скрытую папку можно войти напрямую: из списка она убрана, но путь к
    /// ней остаётся рабочим, и её содержимое читается.
    /// </summary>
    [Test]
    public void List_InsideHiddenDirectory_StillReadsContent()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git", "hooks"));

        var listing = _browser.List(Path.Combine(_root, ".git"));

        Assert.Multiple(() =>
        {
            Assert.That(listing.Exists, Is.True);
            Assert.That(listing.Folders, Is.EqualTo(new[] { "hooks" }));
        });
    }

    /// <summary>
    /// Несуществующий путь, файл и пустая строка дают один и тот же пустой результат.
    /// </summary>
    [Test]
    public void List_UnusablePath_ReportsMissing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_browser.List(Path.Combine(_root, "ghost")).Exists, Is.False);
            Assert.That(_browser.List(Path.Combine(_root, "notes.txt")).Exists, Is.False);
            Assert.That(_browser.List(string.Empty).Exists, Is.False);
            Assert.That(_browser.List(null).Folders, Is.Empty);
        });
    }

    /// <summary>
    /// Имя с пробелами по краям доходит от файловой системы до собранного пути
    /// без потерь: по такому пути каталог обязан находиться.
    /// </summary>
    [Test]
    public void List_NameWithEdgeSpaces_ComposesUsablePath()
    {
        var name = " spaced dir ";
        Directory.CreateDirectory(Path.Combine(_root, name));

        var listing = _browser.List(_root);
        var composed = PathUtilities.Child(_root, listing.Folders.Single(folder => folder.Contains("spaced")));

        Assert.Multiple(() =>
        {
            Assert.That(listing.Folders, Does.Contain(name));
            Assert.That(Directory.Exists(composed), Is.True, $"собранный путь не ведёт в каталог: «{composed}»");
        });
    }

    /// <summary>
    /// Каталог без подкаталогов — пустой список при существующем пути.
    /// </summary>
    [Test]
    public void List_EmptyDirectory_ExistsWithoutFolders()
    {
        var empty = Path.Combine(_root, "web");

        var listing = _browser.List(empty);

        Assert.Multiple(() =>
        {
            Assert.That(listing.Exists, Is.True);
            Assert.That(listing.Folders, Is.Empty);
        });
    }
}
