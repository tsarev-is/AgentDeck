using AgentDeck.Layout;
using NUnit.Framework;

namespace AgentDeck.Tests.Layout;

/// <summary>
/// Резолвер целей перетаскивания: зоны, приоритеты, подавление и точность превью.
/// </summary>
[TestFixture]
public class DropZoneResolverTests
{
    private static readonly RectD Bounds = new(0, 0, 1000, 800);

    /// <summary>
    /// Бросок в центр тайла — обмен местами.
    /// </summary>
    [Test]
    public void Resolve_TileCenter_ReturnsSwap()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(4);
        var target = ids[3];
        var rect = tree.RectOf(target, Bounds)!.Value;

        var result = DropZoneResolver.Resolve(Center(rect).X, Center(rect).Y, tree, Bounds, ids[0]);

        Assert.That(result, Is.EqualTo(DropTarget.Swap(target)));
    }

    /// <summary>
    /// Бросок у грани тайла — сплит этой грани.
    /// </summary>
    [TestCase(0.05, 0.5, DockSide.Left)]
    [TestCase(0.95, 0.5, DockSide.Right)]
    [TestCase(0.5, 0.05, DockSide.Top)]
    [TestCase(0.5, 0.95, DockSide.Bottom)]
    public void Resolve_TileEdge_ReturnsEdgeSplit(double fx, double fy, DockSide expected)
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(2);
        var target = ids[1];
        var rect = tree.RectOf(target, Bounds)!.Value;

        var result = DropZoneResolver.Resolve(
            rect.X + fx * rect.W,
            rect.Y + fy * rect.H,
            tree,
            Bounds,
            ids[0]);

        Assert.That(result, Is.EqualTo(DropTarget.EdgeSplit(target, expected)));
    }

    /// <summary>
    /// Полоса у края окна приоритетнее зон тайла: точка попадает и в тайл, и в полосу.
    /// </summary>
    [TestCase(5.0, 400.0, DockSide.Left)]
    [TestCase(995.0, 400.0, DockSide.Right)]
    [TestCase(500.0, 5.0, DockSide.Top)]
    [TestCase(500.0, 795.0, DockSide.Bottom)]
    public void Resolve_WindowEdgeBand_TakesPriorityOverTileZones(double x, double y, DockSide expected)
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(2);

        var result = DropZoneResolver.Resolve(x, y, tree, Bounds, ids[0]);

        Assert.That(result, Is.EqualTo(DropTarget.Dock(expected)));
    }

    /// <summary>
    /// Сразу за полосой края окна снова работают зоны тайлов.
    /// </summary>
    [Test]
    public void Resolve_JustInsideWindowEdgeBand_FallsBackToTileZone()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(2);
        var x = DropZoneResolver.WindowEdgeBand + 1;

        var result = DropZoneResolver.Resolve(x, 400, tree, Bounds, ids[1]);

        Assert.That(result, Is.EqualTo(DropTarget.EdgeSplit(ids[0], DockSide.Left)));
    }

    /// <summary>
    /// Бросок на самого себя целью не является.
    /// </summary>
    [Test]
    public void Resolve_OnDraggedTileItself_ReturnsNull()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(2);
        var rect = tree.RectOf(ids[0], Bounds)!.Value;

        Assert.That(DropZoneResolver.Resolve(Center(rect).X, Center(rect).Y, tree, Bounds, ids[0]), Is.Null);
    }

    /// <summary>
    /// Цель подавляется, если сплит нарушит минимальный размер тайла.
    /// </summary>
    [Test]
    public void Resolve_EdgeSplitViolatingMinSize_IsSuppressed()
    {
        var narrow = Guid.NewGuid();
        var wide = Guid.NewGuid();
        var mover = Guid.NewGuid();

        // Узкая колонка 0.16: половина 0.08 меньше MinWidth 0.14.
        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(narrow), 0.16),
            new SplitChild(new LeafNode(wide), 0.68),
            new SplitChild(new LeafNode(mover), 0.16),
        ]));

        var rect = tree.RectOf(narrow, Bounds)!.Value;

        var result = DropZoneResolver.Resolve(rect.X + 0.05 * rect.W, rect.Y + 0.5 * rect.H, tree, Bounds, mover);

        Assert.That(result, Is.Null, "сплит узкой колонки должен подавляться");
    }

    /// <summary>
    /// Обмен местами не подавляется никогда: геометрия дерева не меняется.
    /// </summary>
    [Test]
    public void Resolve_SwapIsNeverSuppressed()
    {
        var narrow = Guid.NewGuid();
        var mover = Guid.NewGuid();

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(narrow), 0.15),
            new SplitChild(new LeafNode(mover), 0.85),
        ]));

        var rect = tree.RectOf(narrow, Bounds)!.Value;

        Assert.That(
            DropZoneResolver.Resolve(Center(rect).X, Center(rect).Y, tree, Bounds, mover),
            Is.EqualTo(DropTarget.Swap(narrow)));
    }

    /// <summary>
    /// Ключевой инвариант подсветки: превью совпадает с прямоугольником,
    /// который тайл реально получит после применения мутации.
    /// </summary>
    [Test]
    public void PreviewRect_MatchesGeometryAfterApply([Values(2, 3, 4, 5, 6)] int tileCount)
    {
        var (source, ids) = LayoutTestHelpers.BuildAuto(tileCount);
        var mover = ids[^1];

        foreach (var target in ids.Take(ids.Count - 1))
        {
            foreach (var side in Enum.GetValues<DockSide>())
            {
                AssertPreviewMatchesApply(source, DropTarget.EdgeSplit(target, side), mover);
                AssertPreviewMatchesApply(source, DropTarget.Swap(target), mover);
            }
        }

        foreach (var side in Enum.GetValues<DockSide>())
        {
            AssertPreviewMatchesApply(source, DropTarget.Dock(side), mover);
        }
    }

    /// <summary>
    /// Превью каждой цели, найденной резолвером по сетке точек, совпадает с фактом.
    /// </summary>
    [Test]
    public void PreviewRect_MatchesApply_ForEveryResolvedPointOnGrid()
    {
        var (source, ids) = LayoutTestHelpers.BuildAuto(5);
        var mover = ids[2];
        var checkedTargets = 0;

        for (var x = 2.0; x < Bounds.W; x += 37)
        {
            for (var y = 2.0; y < Bounds.H; y += 31)
            {
                if (DropZoneResolver.Resolve(x, y, source, Bounds, mover) is not { } target)
                {
                    continue;
                }

                AssertPreviewMatchesApply(source, target, mover);
                checkedTargets++;
            }
        }

        Assert.That(checkedTargets, Is.GreaterThan(100), "сетка должна покрыть множество целей");
    }

    /// <summary>
    /// Перенос сохраняет число тайлов и все инварианты раскладки.
    /// </summary>
    [Test]
    public void Apply_EdgeSplit_KeepsTileCountAndInvariants()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(4);
        var mover = ids[0];
        var target = ids[3];

        Assert.That(DropZoneResolver.Apply(tree, DropTarget.EdgeSplit(target, DockSide.Bottom), mover), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.LeafCount, Is.EqualTo(4), "перенос не меняет число тайлов");
            Assert.That(tree.TileIds.OrderBy(x => x), Is.EqualTo(ids.OrderBy(x => x)), "состав тайлов сохраняется");
        });

        LayoutTestHelpers.AssertInvariants(tree, "после переноса");
    }

    /// <summary>
    /// Старое место переехавшего тайла поглощается соседом — дыры не остаётся.
    /// </summary>
    [Test]
    public void Apply_Dock_LeavesNoHole()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(3);

        Assert.That(DropZoneResolver.Apply(tree, DropTarget.Dock(DockSide.Bottom), ids[0]), Is.True);

        Assert.That(tree.Project().Sum(t => t.Rect.Area), Is.EqualTo(1.0).Within(1e-9));
        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// Обмен местами меняет ровно два прямоугольника.
    /// </summary>
    [Test]
    public void Apply_Swap_ExchangesRects()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(4);
        var first = tree.RectOf(ids[0], Bounds)!.Value;
        var second = tree.RectOf(ids[2], Bounds)!.Value;

        Assert.That(DropZoneResolver.Apply(tree, DropTarget.Swap(ids[2]), ids[0]), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.RectOf(ids[0], Bounds), Is.EqualTo(second));
            Assert.That(tree.RectOf(ids[2], Bounds), Is.EqualTo(first));
        });
    }

    /// <summary>
    /// Применение к отсутствующему тайлу — no-op.
    /// </summary>
    [Test]
    public void Apply_UnknownTile_ReturnsFalse()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(2);

        Assert.That(DropZoneResolver.Apply(tree, DropTarget.Swap(ids[0]), Guid.NewGuid()), Is.False);
    }

    /// <summary>
    /// В пустом дереве целей нет.
    /// </summary>
    [Test]
    public void Resolve_EmptyTree_ReturnsNull()
    {
        Assert.That(DropZoneResolver.Resolve(500, 400, new LayoutTree(), Bounds, Guid.NewGuid()), Is.Null);
    }

    private static void AssertPreviewMatchesApply(LayoutTree source, DropTarget target, Guid mover)
    {
        var preview = DropZoneResolver.PreviewRect(target, source, Bounds, mover);

        var applied = source.Clone();
        if (!DropZoneResolver.Apply(applied, target, mover))
        {
            Assert.That(preview, Is.Null, $"превью должно отсутствовать, если мутация невозможна: {target}");
            return;
        }

        Assert.That(preview, Is.Not.Null, $"превью отсутствует для достижимой цели {target}");
        Assert.That(
            applied.RectOf(mover, Bounds),
            Is.EqualTo(preview),
            $"превью разошлось с фактической геометрией для {target.Kind}/{target.Side}");
    }

    private static (double X, double Y) Center(RectD rect) => (rect.X + rect.W / 2, rect.Y + rect.H / 2);
}
