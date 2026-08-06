using AgentDeck.Layout;
using AgentDeck.Models;
using AgentDeck.Session;
using AgentDeck.Settings;
using AgentDeck.ViewModels;
using NUnit.Framework;

namespace AgentDeck.Tests.Session;

/// <summary>
/// Capture/Restore дека: геометрия, директории и агенты переживают перезапуск.
/// </summary>
[TestFixture]
public class SessionRestoreTests
{
    private static readonly RectD Bounds = new(0, 0, 1280, 800);

    /// <summary>
    /// Дек с нетривиальной раскладкой восстанавливается прямоугольник в прямоугольник.
    /// </summary>
    [Test]
    public void CaptureThenRestore_ReproducesGeometryDirectoriesAndAgents()
    {
        var source = BuildDeck();
        var state = source.CaptureSession();

        var restored = new DeckViewModel();
        Assert.That(restored.RestoreSession(state), Is.True);

        Assert.That(restored.Tiles, Has.Count.EqualTo(source.Tiles.Count));

        foreach (var original in source.Tiles)
        {
            var tile = restored.FindTile(original.Id);

            Assert.That(tile, Is.Not.Null, $"тайл {original.Id} должен восстановиться");
            Assert.Multiple(() =>
            {
                Assert.That(tile!.Directory, Is.EqualTo(original.Directory));
                Assert.That(tile.UtilityName, Is.EqualTo(original.UtilityName));
                Assert.That(
                    restored.Layout.RectOf(original.Id, Bounds),
                    Is.EqualTo(source.Layout.RectOf(original.Id, Bounds)),
                    $"геометрия тайла {original.Id}");
            });
        }
    }

    /// <summary>
    /// Восстановленные тайлы — плейсхолдеры без процессов, с акцентом на сохранённом агенте.
    /// </summary>
    [Test]
    public void Restore_ProducesPlaceholdersWithSuggestedAgent()
    {
        var source = BuildDeck();
        var restored = new DeckViewModel();
        restored.RestoreSession(source.CaptureSession());

        foreach (var tile in restored.Tiles)
        {
            Assert.Multiple(() =>
            {
                Assert.That(tile.IsPlaceholder, Is.True, "процессы не должны запускаться автоматически");
                Assert.That(tile.Terminal, Is.Null);
                Assert.That(
                    tile.LaunchOptions.Count(o => o.IsSuggested),
                    Is.EqualTo(tile.UtilityName is null ? 0 : 1),
                    "акцентируется ровно кнопка сохранённой утилиты");
            });
        }
    }

    /// <summary>
    /// Перенос тайлов до сохранения тоже переживает round-trip.
    /// </summary>
    [Test]
    public void CaptureAfterMove_RestoresMovedLayout()
    {
        var source = BuildDeck();
        var mover = source.Tiles[0].Id;
        var target = source.Tiles[3].Id;

        Assert.That(DropZoneResolver.Apply(source.Layout, DropTarget.EdgeSplit(target, DockSide.Bottom), mover), Is.True);

        var restored = new DeckViewModel();
        Assert.That(restored.RestoreSession(source.CaptureSession()), Is.True);

        foreach (var tile in source.Tiles)
        {
            Assert.That(
                restored.Layout.RectOf(tile.Id, Bounds),
                Is.EqualTo(source.Layout.RectOf(tile.Id, Bounds)),
                $"геометрия тайла {tile.Id} после переноса");
        }
    }

    /// <summary>
    /// Тайл, отсутствующий в дереве, отбрасывается, а раскладка остаётся валидной.
    /// </summary>
    [Test]
    public void Restore_TileMissingFromLayout_IsDropped()
    {
        var source = BuildDeck();
        var state = source.CaptureSession();

        state.Tiles.Add(new TileState
        {
            Id = Guid.NewGuid().ToString(),
            Directory = "/ghost",
            AgentKind = nameof(AgentKind.Codex),
        });

        var restored = new DeckViewModel();
        Assert.That(restored.RestoreSession(state), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Tiles, Has.Count.EqualTo(source.Tiles.Count), "лишний тайл должен отброситься");
            Assert.That(restored.Tiles.Any(t => t.Directory == "/ghost"), Is.False);
            Assert.That(restored.Layout.Validate(), Is.True);
        });
    }

    /// <summary>
    /// Лист дерева без описания тайла отбрасывается вместе с местом,
    /// которое поглощают соседи.
    /// </summary>
    [Test]
    public void Restore_LayoutLeafWithoutTileState_IsDropped()
    {
        var source = BuildDeck();
        var state = source.CaptureSession();
        var removed = state.Tiles[1];
        state.Tiles.Remove(removed);

        var restored = new DeckViewModel();
        Assert.That(restored.RestoreSession(state), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Tiles, Has.Count.EqualTo(source.Tiles.Count - 1));
            Assert.That(restored.Layout.Contains(Guid.Parse(removed.Id!)), Is.False);
            Assert.That(restored.Layout.Validate(), Is.True);
            Assert.That(restored.Layout.Project().Sum(t => t.Rect.Area), Is.EqualTo(1.0).Within(1e-9));
        });
    }

    /// <summary>
    /// Повреждённое или пустое состояние даёт чистый старт.
    /// </summary>
    [Test]
    public void Restore_InvalidState_ReturnsFalse()
    {
        var deck = new DeckViewModel();

        Assert.Multiple(() =>
        {
            Assert.That(deck.RestoreSession(null), Is.False);
            Assert.That(deck.RestoreSession(new SessionState()), Is.False, "нулевая версия формата");
            Assert.That(
                deck.RestoreSession(new SessionState { LayoutVersion = LayoutSerializer.CurrentVersion + 1 }),
                Is.False);
        });
    }

    /// <summary>
    /// Состояние с числом тайлов сверх капа отбрасывается целиком.
    /// </summary>
    [Test]
    public void Restore_MoreTilesThanCap_ReturnsFalse()
    {
        var tree = new LayoutTree();
        var tiles = new List<TileState>();

        for (var i = 0; i < LayoutConstants.MaxTiles + 1; i++)
        {
            var id = Guid.NewGuid();

            // Обходим AddTileAuto: он сам упёрся бы в min-size.
            tree = new LayoutTree(tree.Root is null
                ? new LeafNode(id)
                : new SplitNode(Orientation.Vertical,
                [
                    new SplitChild(tree.Root, 0.9),
                    new SplitChild(new LeafNode(id), 0.1),
                ]));

            tiles.Add(new TileState { Id = id.ToString(), Directory = "/tmp" });
        }

        var state = new SessionState
        {
            LayoutVersion = LayoutSerializer.CurrentVersion,
            Layout = LayoutSerializer.ToDto(tree).Root,
            Tiles = tiles,
        };

        Assert.That(new DeckViewModel().RestoreSession(state), Is.False);
    }

    /// <summary>
    /// Пустой дек сохраняется и восстанавливается как пустой.
    /// </summary>
    [Test]
    public void CaptureThenRestore_EmptyDeck_StaysEmpty()
    {
        var restored = new DeckViewModel();

        Assert.That(restored.RestoreSession(new DeckViewModel().CaptureSession()), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(restored.IsEmpty, Is.True);
            Assert.That(restored.Layout.Root, Is.Null);
        });
    }

    /// <summary>
    /// После восстановления дек продолжает работать: можно добавлять и закрывать тайлы.
    /// </summary>
    [Test]
    public void Restore_LeavesDeckOperational()
    {
        var source = BuildDeck();
        var restored = new DeckViewModel();
        restored.RestoreSession(source.CaptureSession());

        var added = restored.AddTile();
        Assert.That(added, Is.Not.Null);

        Assert.That(restored.CloseTile(restored.Tiles[0].Id), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Layout.Validate(), Is.True);
            Assert.That(restored.Layout.LeafCount, Is.EqualTo(restored.Tiles.Count));
        });
    }

    /// <summary>
    /// Переименованная в настройках утилита остаётся собой: восстановленный тайл
    /// находит её кнопку по идентификатору, а не по подписи. По имени подсказка
    /// после переименования не находила бы уже ничего.
    /// </summary>
    [Test]
    public void Restore_AfterUtilityRenamed_HighlightsSameUtility()
    {
        var settings = new AppSettings
        {
            Utilities = [new UtilityState { Id = "u-1", Name = "codex", Command = "codex", Enabled = true }],
        };

        var source = new DeckViewModel(settings);
        var tile = source.AddTile("/tmp", null)!;
        tile.SuggestAgent("u-1", "codex");

        var renamed = new AppSettings
        {
            Utilities = [new UtilityState { Id = "u-1", Name = "codex-preview", Command = "codex", Enabled = true }],
        };

        var restored = new DeckViewModel(renamed);
        Assert.That(restored.RestoreSession(source.CaptureSession()), Is.True);

        var suggested = restored.FindTile(tile.Id)!.LaunchOptions.Single(o => o.IsSuggested);
        Assert.That(suggested.Name, Is.EqualTo("codex-preview"), "акцент уехал вместе с переименованной утилитой");
    }

    /// <summary>
    /// Удалённую утилиту не подменяет её тёзка: сохранён был идентификатор, и
    /// новая утилита с тем же именем — другая утилита.
    /// </summary>
    [Test]
    public void Restore_AfterUtilityRemoved_HighlightsNothing()
    {
        var source = new DeckViewModel(new AppSettings
        {
            Utilities = [new UtilityState { Id = "u-1", Name = "codex", Command = "codex", Enabled = true }],
        });

        var tile = source.AddTile("/tmp", null)!;
        tile.SuggestAgent("u-1", "codex");

        var restored = new DeckViewModel(new AppSettings
        {
            Utilities = [new UtilityState { Id = "u-2", Name = "codex", Command = "codex", Enabled = true }],
        });

        Assert.That(restored.RestoreSession(source.CaptureSession()), Is.True);
        Assert.That(restored.FindTile(tile.Id)!.LaunchOptions.Any(o => o.IsSuggested), Is.False);
    }

    /// <summary>
    /// Собирает дек из пяти тайлов с разными агентами и нетривиальной раскладкой.
    /// </summary>
    private static DeckViewModel BuildDeck()
    {
        var deck = new DeckViewModel();
        string?[] utilities = ["claude", "codex", null, "script", "cursor-agent"];

        for (var i = 0; i < utilities.Length; i++)
        {
            var tile = deck.AddTile($"/home/user/dev/project-{i}", utilities[i]);
            Assert.That(tile, Is.Not.Null);
        }

        // Нетривиальные доли: ресайз корневого сплиттера.
        if (deck.Layout.Root is SplitNode root)
        {
            deck.Layout.ResizeSplitter(root, 0, 0.07);
        }

        return deck;
    }
}
