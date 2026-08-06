using System.Text.Json;
using AgentDeck.Layout;
using AgentDeck.Session;
using NUnit.Framework;

namespace AgentDeck.Tests.Session;

/// <summary>
/// Хранилище сессии: round-trip, устойчивость к повреждениям, атомарность записи.
/// </summary>
[TestFixture]
public class SessionStoreTests
{
    private string _root = null!;
    private SessionStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"agentdeck-session-{Guid.NewGuid():N}");
        _store = new SessionStore(_root);
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
    /// Сохранённое состояние читается без потерь.
    /// </summary>
    [Test]
    public void RoundTrip_PreservesLayoutTilesAndWindow()
    {
        var state = BuildState();

        Assert.That(_store.Save(state), Is.True);

        var loaded = _store.Load();

        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.LayoutVersion, Is.EqualTo(LayoutSerializer.CurrentVersion));
            Assert.That(loaded.Tiles, Has.Count.EqualTo(state.Tiles.Count));
            Assert.That(loaded.Tiles[0].Id, Is.EqualTo(state.Tiles[0].Id));
            Assert.That(loaded.Tiles[0].Directory, Is.EqualTo(state.Tiles[0].Directory));

            // Идентификатор утилиты обязан дожить до файла и обратно: именно по
            // нему кнопка находится снова, а имя рядом с ним — лишь для сессий,
            // записанных до его появления.
            Assert.That(loaded.Tiles[0].UtilityId, Is.EqualTo(state.Tiles[0].UtilityId));
            Assert.That(loaded.Tiles[0].Utility, Is.EqualTo(state.Tiles[0].Utility));
            Assert.That(loaded.Tiles[0].AgentKind, Is.EqualTo(state.Tiles[0].AgentKind));
            Assert.That(loaded.Window!.Width, Is.EqualTo(state.Window!.Width));
            Assert.That(loaded.Window.X, Is.EqualTo(state.Window.X));
            Assert.That(loaded.Window.Maximized, Is.EqualTo(state.Window.Maximized));
        });

        // Дерево должно восстанавливаться до той же проекции.
        var tree = LayoutSerializer.FromDto(new LayoutDocumentDto
        {
            LayoutVersion = loaded.LayoutVersion,
            Root = loaded.Layout,
        });

        Assert.That(tree, Is.Not.Null);
        Assert.That(tree!.LeafCount, Is.EqualTo(2));
    }

    /// <summary>
    /// Файл кладётся в подкаталог приложения.
    /// </summary>
    [Test]
    public void Save_WritesIntoApplicationFolder()
    {
        _store.Save(BuildState());

        Assert.Multiple(() =>
        {
            Assert.That(_store.FilePath, Does.EndWith(Path.Combine(SessionStore.FolderName, SessionStore.FileName)));
            Assert.That(File.Exists(_store.FilePath), Is.True);
        });
    }

    /// <summary>
    /// Отсутствие файла — это чистый старт, а не ошибка.
    /// </summary>
    [Test]
    public void Load_MissingFile_ReturnsNull()
    {
        Assert.That(_store.Load(), Is.Null);
    }

    /// <summary>
    /// Мусорный, обрезанный и пустой файл дают null.
    /// </summary>
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("это не json")]
    [TestCase("{\"layoutVersion\": 1, \"tiles\": [")]
    [TestCase("[1,2,3]")]
    public void Load_CorruptFile_ReturnsNull(string content)
    {
        Directory.CreateDirectory(_store.Directory);
        File.WriteAllText(_store.FilePath, content);

        Assert.That(_store.Load(), Is.Null);
    }

    /// <summary>
    /// Чужая версия формата отбрасывает весь файл.
    /// </summary>
    [TestCase(0)]
    [TestCase(2)]
    [TestCase(99)]
    public void Load_ForeignLayoutVersion_ReturnsNull(int version)
    {
        var state = BuildState();
        state.LayoutVersion = version;
        _store.Save(state);

        Assert.That(_store.Load(), Is.Null);
    }

    /// <summary>
    /// Запись атомарна: старый файл заменяется целиком, временный не остаётся.
    /// </summary>
    [Test]
    public void Save_IsAtomic_ReplacesFileWholeAndLeavesNoTemporary()
    {
        _store.Save(BuildState());
        var first = File.ReadAllText(_store.FilePath);

        var second = BuildState();
        second.Tiles.Add(new TileState { Id = Guid.NewGuid().ToString(), Directory = "/srv/extra" });
        _store.Save(second);

        var content = File.ReadAllText(_store.FilePath);

        Assert.Multiple(() =>
        {
            Assert.That(content, Is.Not.EqualTo(first), "файл должен обновиться");
            Assert.That(content, Does.Contain("/srv/extra"));
            Assert.That(File.Exists(_store.FilePath + ".tmp"), Is.False, "временный файл не должен оставаться");
            Assert.That(Directory.GetFiles(_store.Directory), Has.Length.EqualTo(1), "в каталоге только session.json");
        });

        // Итоговый файл — валидный JSON целиком, без хвоста от предыдущей записи.
        Assert.DoesNotThrow(() => JsonSerializer.Deserialize<SessionState>(content, LayoutSerializer.JsonOptions));
    }

    /// <summary>
    /// Повторное сохранение поверх существующего файла не падает.
    /// </summary>
    [Test]
    public void Save_Repeatedly_Succeeds()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.That(_store.Save(BuildState()), Is.True, $"сохранение #{i}");
        }

        Assert.That(_store.Load(), Is.Not.Null);
    }

    /// <summary>
    /// Каталог создаётся при первой записи.
    /// </summary>
    [Test]
    public void Save_CreatesDirectory()
    {
        Assert.That(Directory.Exists(_store.Directory), Is.False);

        _store.Save(BuildState());

        Assert.That(Directory.Exists(_store.Directory), Is.True);
    }

    /// <summary>
    /// Удаление файла возвращает хранилище к чистому состоянию.
    /// </summary>
    [Test]
    public void Delete_RemovesFile()
    {
        _store.Save(BuildState());
        _store.Delete();

        Assert.That(_store.Load(), Is.Null);
    }

    /// <summary>
    /// Путь по умолчанию ведёт в каталог конфигурации пользователя.
    /// </summary>
    [Test]
    public void DefaultStore_UsesUserConfigDirectory()
    {
        var store = new SessionStore();

        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathRooted(store.FilePath), Is.True);
            Assert.That(store.FilePath, Does.Contain(SessionStore.FolderName));
            Assert.That(store.FilePath, Does.EndWith(SessionStore.FileName));
        });
    }

    private static SessionState BuildState()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(first), 0.4),
            new SplitChild(new LeafNode(second), 0.6),
        ]));

        return new SessionState
        {
            LayoutVersion = LayoutSerializer.CurrentVersion,
            Layout = LayoutSerializer.ToDto(tree).Root,
            Tiles =
            [
                new TileState
                {
                    Id = first.ToString(),
                    Directory = "/home/user/dev/api",
                    UtilityId = "u-1",
                    Utility = "claude",
                    AgentKind = "Claude",
                },
                new TileState { Id = second.ToString(), Directory = "/home/user/dev/web", AgentKind = null },
            ],
            Window = new AgentDeck.Session.WindowState { X = 120, Y = 80, Width = 1400, Height = 900, Maximized = false },
        };
    }
}
