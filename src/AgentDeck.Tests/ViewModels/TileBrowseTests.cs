using System.Collections.Concurrent;
using AgentDeck.Models;
using AgentDeck.Status;
using AgentDeck.ViewModels;
using NUnit.Framework;

namespace AgentDeck.Tests.ViewModels;

/// <summary>
/// Выбор рабочей папки в новом тайле: чтение вложенных каталогов, поиск,
/// переходы вниз и вверх.
/// </summary>
[TestFixture]
public class TileBrowseTests
{
    /// <summary>
    /// Каталог по умолчанию для тестов: папки в том виде, в котором их отдаёт
    /// <see cref="DirectoryBrowser"/> — скрытые он отсекает сам.
    /// </summary>
    private static readonly DirectoryListing Work = new(["api-gateway", "billing", "web"], Exists: true);

    /// <summary>
    /// Каталог, который не влезает в показанную порцию: «dir-000», «dir-001», …
    /// </summary>
    /// <param name="count">
    /// Сколько папок в каталоге.
    /// </param>
    private static DirectoryListing Many(int count)
        => new([.. Enumerable.Range(0, count).Select(index => $"dir-{index:000}")], Exists: true);

    /// <summary>
    /// Плейсхолдер показывает прочитанные папки, не отсеивая ничего сверх того,
    /// что уже отсеял обзор каталога.
    /// </summary>
    [Test]
    public async Task BeginBrowse_ShowsListedFolders()
    {
        var tile = CreateTile("/work", _ => Work);

        tile.BeginBrowse();
        await tile.BrowseTask;

        Assert.Multiple(() =>
        {
            Assert.That(tile.Folders.Select(f => f.Name), Is.EqualTo(new[] { "api-gateway", "billing", "web" }));
            Assert.That(tile.Folders[0].FullPath, Is.EqualTo("/work/api-gateway"));
            Assert.That(tile.HasFolders, Is.True);
        });
    }

    /// <summary>
    /// Каждый символ фильтра сужает список, регистр не важен, каталог заново
    /// не читается.
    /// </summary>
    [Test]
    public async Task Filter_NarrowsFoldersWithoutRereadingDirectory()
    {
        var browser = new StubBrowser(_ => Work);
        var tile = CreateTile("/work", browser);

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.Filter = "BIL";

        Assert.Multiple(() =>
        {
            Assert.That(tile.Folders.Select(f => f.Name), Is.EqualTo(new[] { "billing" }));
            Assert.That(browser.Requests, Has.Count.EqualTo(1), "фильтр не должен трогать файловую систему");
        });
    }

    /// <summary>
    /// Ничего не найдено — список пуст и об этом сказано.
    /// </summary>
    [Test]
    public async Task Filter_WithoutMatches_ReportsHint()
    {
        var tile = CreateTile("/work", _ => Work);

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.Filter = "zzz";

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasFolders, Is.False);
            Assert.That(tile.FolderHint, Is.EqualTo("no matching folders"));
        });
    }

    /// <summary>
    /// Пустой каталог сообщает, что заходить некуда.
    /// </summary>
    [Test]
    public async Task BeginBrowse_EmptyDirectory_ReportsHint()
    {
        var tile = CreateTile("/work", _ => new DirectoryListing([], Exists: true));

        tile.BeginBrowse();
        await tile.BrowseTask;

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasFolders, Is.False);
            Assert.That(tile.FolderHint, Is.EqualTo("no folders inside"));
        });
    }

    /// <summary>
    /// Несуществующий путь сообщает об этом до попытки запуска.
    /// </summary>
    [Test]
    public async Task BeginBrowse_MissingDirectory_ReportsHint()
    {
        var tile = CreateTile("/ghost", _ => DirectoryListing.Missing);

        tile.BeginBrowse();
        await tile.BrowseTask;

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasFolders, Is.False);
            Assert.That(tile.FolderHint, Is.EqualTo("directory not found"));
        });
    }

    /// <summary>
    /// Длинный каталог показывается порцией: первые восемь папок и чип с числом
    /// скрытых в конце полосы.
    /// </summary>
    [Test]
    public async Task BeginBrowse_LongListing_ShowsFirstPageWithMoreChip()
    {
        var tile = CreateTile("/work", _ => Many(30));

        tile.BeginBrowse();
        await tile.BrowseTask;

        var more = tile.MoreFolders;

        Assert.Multiple(() =>
        {
            Assert.That(tile.Folders.Select(folder => folder.Name), Is.EqualTo(new[]
            {
                "dir-000", "dir-001", "dir-002", "dir-003",
                "dir-004", "dir-005", "dir-006", "dir-007",
            }));
            Assert.That(more, Is.Not.Null);
            Assert.That(more?.Count, Is.EqualTo(22));
            Assert.That(more?.Label, Is.EqualTo("… 22 more"));
            Assert.That(tile.FolderChips, Has.Count.EqualTo(9));
            Assert.That(tile.FolderChips[^1], Is.SameAs(more), "чип «ещё» замыкает полосу");
            Assert.That(tile.HasFolders, Is.True);
        });
    }

    /// <summary>
    /// Каталог ровно на порцию сворачивать нечего — чипа «ещё» нет.
    /// </summary>
    [Test]
    public async Task BeginBrowse_ListingFitsFirstPage_HasNoMoreChip()
    {
        var tile = CreateTile("/work", _ => Many(TileViewModel.DefaultFolderCap));

        tile.BeginBrowse();
        await tile.BrowseTask;

        Assert.Multiple(() =>
        {
            Assert.That(tile.Folders, Has.Count.EqualTo(TileViewModel.DefaultFolderCap));
            Assert.That(tile.MoreFolders, Is.Null);
            Assert.That(tile.FolderChips, Has.Count.EqualTo(TileViewModel.DefaultFolderCap));
        });
    }

    /// <summary>
    /// Поиск идёт по всему каталогу, а не по свёрнутому списку: папка из глубины
    /// листинга находится, хотя её чипа на экране не было.
    /// </summary>
    [Test]
    public async Task Filter_MatchBeyondFirstPage_FindsFolderWithoutRereadingDirectory()
    {
        var browser = new StubBrowser(_ => Many(30));
        var tile = CreateTile("/work", browser);

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.Filter = "DIR-025";

        Assert.Multiple(() =>
        {
            Assert.That(tile.Folders.Select(folder => folder.Name), Is.EqualTo(new[] { "dir-025" }));
            Assert.That(tile.Folders[0].FullPath, Is.EqualTo("/work/dir-025"));
            Assert.That(tile.MoreFolders, Is.Null);
            Assert.That(browser.Requests, Has.Count.EqualTo(1), "поиск не должен трогать файловую систему");
        });
    }

    /// <summary>
    /// Порция действует и на найденное, а скрытыми считаются только совпадения.
    /// </summary>
    [Test]
    public async Task Filter_MoreMatchesThanFirstPage_CountsOnlyMatches()
    {
        var tile = CreateTile("/work", _ => Many(30));

        tile.BeginBrowse();
        await tile.BrowseTask;

        // «dir-01» подходит десяти папкам: dir-010 … dir-019.
        tile.Filter = "dir-01";

        Assert.Multiple(() =>
        {
            Assert.That(tile.Folders, Has.Count.EqualTo(8));
            Assert.That(tile.MoreFolders?.Count, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// Нажатие «ещё» добавляет очередную порцию, не перечитывая каталог, и на
    /// последней порции чип исчезает.
    /// </summary>
    [Test]
    public async Task ShowMoreFolders_GrowsVisiblePageByStep()
    {
        var browser = new StubBrowser(_ => Many(30));
        var tile = CreateTile("/work", browser);

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.ShowMoreFolders();

        Assert.Multiple(() =>
        {
            Assert.That(tile.Folders, Has.Count.EqualTo(24));
            Assert.That(tile.MoreFolders?.Count, Is.EqualTo(6));
        });

        tile.ShowMoreFolders();

        Assert.Multiple(() =>
        {
            Assert.That(tile.Folders, Has.Count.EqualTo(30));
            Assert.That(tile.MoreFolders, Is.Null);
            Assert.That(browser.Requests, Has.Count.EqualTo(1), "полоса пересобирается из прочитанного");
        });
    }

    /// <summary>
    /// Скрывать нечего — «ещё» ничего не делает и порцию не наращивает.
    /// </summary>
    [Test]
    public async Task ShowMoreFolders_WithoutHiddenFolders_KeepsPage()
    {
        var tile = CreateTile("/work", _ => Work);

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.ShowMoreFolders();

        Assert.Multiple(() =>
        {
            Assert.That(tile.Folders, Has.Count.EqualTo(3));
            Assert.That(tile.MoreFolders, Is.Null);
        });
    }

    /// <summary>
    /// Вход в папку сворачивает список обратно: раскрытая порция относилась к
    /// прежнему каталогу.
    /// </summary>
    [Test]
    public async Task EnterFolder_CollapsesFoldersBackToFirstPage()
    {
        var tile = CreateTile("/work", _ => Many(30));

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.ShowMoreFolders();
        tile.EnterFolder(tile.Folders[0]);
        await tile.BrowseTask;

        Assert.Multiple(() =>
        {
            Assert.That(tile.Directory, Is.EqualTo("/work/dir-000"));
            Assert.That(tile.Folders, Has.Count.EqualTo(8));
            Assert.That(tile.MoreFolders?.Count, Is.EqualTo(22));
        });
    }

    /// <summary>
    /// Список сворачивается на любой смене каталога, а не только по нажатию на
    /// чип: и на набранном руками пути, и на «уровень выше».
    /// </summary>
    [Test]
    public async Task Directory_ChangedWithoutChipClick_CollapsesFoldersBackToFirstPage()
    {
        var typed = CreateTile("/work", _ => Many(30));

        typed.BeginBrowse();
        await typed.BrowseTask;

        typed.ShowMoreFolders();
        typed.Directory = "/other";
        await typed.BrowseTask;

        var up = CreateTile("/work/dir-000", _ => Many(30));

        up.BeginBrowse();
        await up.BrowseTask;

        up.ShowMoreFolders();
        up.GoUp();
        await up.BrowseTask;

        Assert.Multiple(() =>
        {
            Assert.That(typed.Folders, Has.Count.EqualTo(8), "набранный путь тоже сворачивает список");
            Assert.That(up.Folders, Has.Count.EqualTo(8), "«на уровень выше» тоже сворачивает список");
        });
    }

    /// <summary>
    /// Enter в поле поиска уходит в найденную папку, даже если в свёрнутом
    /// списке её не было.
    /// </summary>
    [Test]
    public async Task EnterFirstFolder_MatchBeyondFirstPage_EntersMatch()
    {
        var tile = CreateTile("/work", _ => Many(30));

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.Filter = "dir-025";

        Assert.Multiple(() =>
        {
            Assert.That(tile.EnterFirstFolder(), Is.True);
            Assert.That(tile.Directory, Is.EqualTo("/work/dir-025"));
        });
    }

    /// <summary>
    /// Нажатие на чип уходит внутрь папки, сбрасывает фильтр и перечитывает каталог.
    /// </summary>
    [Test]
    public async Task EnterFolder_AppendsSegmentAndClearsFilter()
    {
        var browser = new StubBrowser(path => path == "/work" ? Work : new DirectoryListing(["src"], Exists: true));
        var tile = CreateTile("/work", browser);

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.Filter = "api";
        tile.EnterFolder(tile.Folders[0]);
        await tile.BrowseTask;

        Assert.Multiple(() =>
        {
            Assert.That(tile.Directory, Is.EqualTo("/work/api-gateway"));
            Assert.That(tile.Filter, Is.Empty);
            Assert.That(tile.Title, Is.EqualTo("api-gateway"));
            Assert.That(tile.Folders.Select(f => f.Name), Is.EqualTo(new[] { "src" }));
            Assert.That(browser.Requests, Does.Contain("/work/api-gateway"));
        });
    }

    /// <summary>
    /// Чип ведёт по своему пути, а не по имени поверх текущей директории: пока
    /// новый каталог читается, на экране остаются чипы прежнего.
    /// </summary>
    [Test]
    public void EnterFolder_FollowsPathOfTheChipItself()
    {
        var tile = CreateTile("/work/billing", _ => Work);

        tile.EnterFolder(new FolderEntryViewModel("src", "/work/api-gateway/src"));

        Assert.That(tile.Directory, Is.EqualTo("/work/api-gateway/src"));
    }

    /// <summary>
    /// Enter в поле поиска входит в первую найденную папку, на пустом списке —
    /// ничего не делает.
    /// </summary>
    [Test]
    public async Task EnterFirstFolder_FollowsFilteredOrder()
    {
        var tile = CreateTile("/work", _ => Work);

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.Filter = "b";

        Assert.Multiple(() =>
        {
            Assert.That(tile.EnterFirstFolder(), Is.True);
            Assert.That(tile.Directory, Is.EqualTo("/work/billing"));
        });

        var empty = CreateTile("/ghost", _ => DirectoryListing.Missing);
        empty.BeginBrowse();
        await empty.BrowseTask;

        Assert.That(empty.EnterFirstFolder(), Is.False);
    }

    /// <summary>
    /// Кнопка «вверх» поднимает директорию, а в корне гаснет.
    /// </summary>
    [Test]
    public void GoUp_MovesToParentAndStopsAtRoot()
    {
        var tile = CreateTile("/work/api-gateway", _ => Work);

        Assert.Multiple(() =>
        {
            Assert.That(tile.CanGoUp, Is.True);
            Assert.That(tile.GoUp(), Is.True);
            Assert.That(tile.Directory, Is.EqualTo("/work"));
            Assert.That(tile.GoUp(), Is.True);
            Assert.That(tile.Directory, Is.EqualTo("/"));
            Assert.That(tile.CanGoUp, Is.False, "из корня подниматься некуда");
            Assert.That(tile.GoUp(), Is.False);
        });
    }

    /// <summary>
    /// Правка пути отменяет предыдущее чтение: медленный каталог не затирает
    /// список актуального.
    /// </summary>
    [Test]
    public async Task Browse_StaleListing_DoesNotOverwriteNewerDirectory()
    {
        using var gate = new SemaphoreSlim(0);
        var browser = new StubBrowser(path =>
        {
            if (path == "/slow")
            {
                gate.Wait();
                return new DirectoryListing(["stale"], Exists: true);
            }

            return Work;
        });

        var tile = CreateTile("/slow", browser);

        tile.BeginBrowse();
        var stale = tile.BrowseTask;

        tile.Directory = "/work";
        var fresh = tile.BrowseTask;

        gate.Release();
        await Task.WhenAll(stale, fresh);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Directory, Is.EqualTo("/work"));
            Assert.That(tile.Folders.Select(f => f.Name), Is.EqualTo(Work.Folders));
        });
    }

    /// <summary>
    /// У запущенного тайла выбор папки выключен: файловая система больше не
    /// опрашивается.
    /// </summary>
    [Test]
    public async Task Browse_StopsWhenTileLeavesPlaceholder()
    {
        var browser = new StubBrowser(_ => Work);
        var tile = CreateTile("/work", browser);

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.Status = TileStatus.Running;
        tile.BeginBrowse();
        tile.Directory = "/other";
        await tile.BrowseTask;

        Assert.Multiple(() =>
        {
            Assert.That(tile.Folders, Is.Empty);
            Assert.That(tile.FolderChips, Is.Empty);
            Assert.That(tile.MoreFolders, Is.Null);
            Assert.That(browser.Requests, Has.Count.EqualTo(1), "живой терминал каталоги не читает");
        });
    }

    /// <summary>
    /// Пока новый каталог читается, чипы прежнего ведут по своим прежним путям.
    /// Иначе клик по чипу, который ещё виден на экране, уходил бы в
    /// несуществующего соседа уже нового каталога.
    /// </summary>
    [Test]
    public async Task EnterFolder_WhileNewListingPending_KeepsPathsOfListedDirectory()
    {
        using var gate = new SemaphoreSlim(0);
        var browser = new StubBrowser(_ => Work, gate);
        var tile = CreateTile("/work", browser);

        tile.BeginBrowse();
        await tile.BrowseTask;

        // Фильтр непустой: его сброс на смене каталога пересобирает полосу на
        // месте — именно в этот момент чипы и могли бы «переехать».
        tile.Filter = "api";
        tile.EnterFolder(tile.Folders[0]);

        var shown = tile.Folders.Select(folder => folder.FullPath).ToArray();

        gate.Release();
        await tile.BrowseTask;

        Assert.That(shown, Is.EqualTo(new[] { "/work/api-gateway", "/work/billing", "/work/web" }));
    }

    /// <summary>
    /// Возврат в плейсхолдер перечитывает каталог: сорвавшийся запуск не должен
    /// оставлять тайл с пустой полосой, ведь представление уже в дереве и
    /// показать его повторно никто не попросит.
    /// </summary>
    [Test]
    public async Task Status_BackToPlaceholder_ReloadsFolders()
    {
        var browser = new StubBrowser(_ => Work);
        var tile = CreateTile("/work", browser);

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.Status = TileStatus.Running;
        tile.Status = TileStatus.Placeholder;
        await tile.BrowseTask;

        Assert.Multiple(() =>
        {
            Assert.That(tile.Folders.Select(folder => folder.Name), Is.EqualTo(Work.Folders));
            Assert.That(tile.HasFolderHint, Is.False, "каталог на месте — жаловаться не на что");
            Assert.That(browser.Requests, Has.Count.EqualTo(2));
        });
    }

    /// <summary>
    /// До первого чтения подсказки нет: о непрочитанном каталоге сказать нечего,
    /// а «directory not found» было бы прямой неправдой.
    /// </summary>
    [Test]
    public void FolderHint_BeforeFirstListing_SaysNothing()
    {
        var tile = CreateTile("/work", _ => Work);

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasFolders, Is.False);
            Assert.That(tile.HasFolderHint, Is.False);
            Assert.That(tile.FolderHint, Is.Empty);
        });
    }

    /// <summary>
    /// Набор пути по буквам схлопывается в одно обращение к файловой системе:
    /// паузу пережидает только последняя правка.
    /// </summary>
    [Test]
    public async Task Directory_EditedRepeatedly_ReadsOnlyLastPath()
    {
        var browser = new StubBrowser(_ => Work);
        var tile = new TileViewModel(
            Guid.NewGuid(),
            "/work",
            browser: browser,
            browseDelay: TimeSpan.FromMilliseconds(120));

        tile.Directory = "/w";
        tile.Directory = "/wo";
        tile.Directory = "/work/api-gateway";
        await tile.BrowseTask;

        Assert.Multiple(() =>
        {
            Assert.That(browser.Requests, Has.Count.EqualTo(1));
            Assert.That(browser.Requests, Does.Contain("/work/api-gateway"));
        });
    }

    /// <summary>
    /// Переход по чипу читает новый каталог ровно один раз: смена пути и переход
    /// не планируют чтение каждый по-своему.
    /// </summary>
    [Test]
    public async Task EnterFolder_ReadsNewDirectoryOnce()
    {
        var browser = new StubBrowser(_ => Work);
        var tile = CreateTile("/work", browser);

        tile.BeginBrowse();
        await tile.BrowseTask;

        tile.EnterFolder(tile.Folders[0]);
        await tile.BrowseTask;

        Assert.That(browser.Requests, Has.Count.EqualTo(2));
    }

    private static TileViewModel CreateTile(string directory, Func<string?, DirectoryListing> list)
        => CreateTile(directory, new StubBrowser(list));

    private static TileViewModel CreateTile(string directory, StubBrowser browser)
        => new(Guid.NewGuid(), directory, browser: browser, browseDelay: TimeSpan.Zero);

    /// <summary>
    /// Файловая система на подмену: отдаёт заданный список и запоминает запросы.
    /// </summary>
    private sealed class StubBrowser : DirectoryBrowser
    {
        private readonly Func<string?, DirectoryListing> _list;
        private readonly SemaphoreSlim? _gate;
        private readonly ConcurrentQueue<string?> _requests = new();

        /// <summary>
        /// Создаёт подменную файловую систему.
        /// </summary>
        /// <param name="list">
        /// Что отдавать на запрошенный путь.
        /// </param>
        /// <param name="gate">
        /// Шлюз, задерживающий все чтения кроме первого: тест успевает
        /// посмотреть на полосу, пока новый каталог ещё читается.
        /// </param>
        public StubBrowser(Func<string?, DirectoryListing> list, SemaphoreSlim? gate = null)
        {
            _list = list;
            _gate = gate;
        }

        /// <summary>
        /// Пути, которые запросили; чтение идёт из пула потоков.
        /// </summary>
        public IReadOnlyCollection<string?> Requests => _requests;

        /// <inheritdoc />
        public override DirectoryListing List(string? expandedPath)
        {
            _requests.Enqueue(expandedPath);

            if (_requests.Count > 1)
            {
                _gate?.Wait();
            }

            return _list(expandedPath);
        }
    }
}
