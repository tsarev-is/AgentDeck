using AgentDeck.Layout;
using NUnit.Framework;

namespace AgentDeck.Tests.Layout;

/// <summary>
/// Семантика переноса тайла: insert на новое место, затем remove старого листа.
/// </summary>
[TestFixture]
public class DragMoveSemanticsTests
{
    private static readonly RectD Bounds = RectD.Unit;

    /// <summary>
    /// Перенос не меняет число и состав тайлов и сохраняет инварианты.
    /// </summary>
    [Test]
    public void Move_KeepsTileSetAndInvariants([Values(2, 3, 5, 8)] int tileCount)
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(tileCount);
        var expected = ids.OrderBy(x => x).ToList();
        var random = new Random(4242);

        for (var step = 0; step < 200; step++)
        {
            var mover = ids[random.Next(ids.Count)];
            var target = ids[random.Next(ids.Count)];
            var side = (DockSide)random.Next(4);

            var drop = random.Next(3) switch
            {
                0 when target != mover => DropTarget.Swap(target),
                1 when tree.CanInsertAtEdge(target, side, mover) => DropTarget.EdgeSplit(target, side),
                2 when tree.CanInsertAtRootEdge(side, mover) => DropTarget.Dock(side),
                _ => (DropTarget?)null,
            };

            if (drop is not { } target2)
            {
                continue;
            }

            DropZoneResolver.Apply(tree, target2, mover);

            LayoutTestHelpers.AssertInvariants(tree, $"шаг {step}, цель {target2.Kind}");
            Assert.That(tree.TileIds.OrderBy(x => x), Is.EqualTo(expected), $"состав тайлов на шаге {step}");
            Assert.That(tree.MinSizeSatisfied(), Is.True, $"минимальный размер на шаге {step}");
        }
    }

    /// <summary>
    /// Старое место переехавшего тайла поглощают сиблинги пропорционально своим
    /// долям: полоса слева закрывается, дыры не остаётся.
    /// </summary>
    [Test]
    public void Move_OldPlaceIsAbsorbedBySiblings()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        // Три колонки; переносим левую вниз от правой.
        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 1.0 / 3),
            new SplitChild(new LeafNode(b), 1.0 / 3),
            new SplitChild(new LeafNode(c), 1.0 / 3),
        ]));

        Assert.That(DropZoneResolver.Apply(tree, DropTarget.EdgeSplit(c, DockSide.Bottom), a), Is.True);

        var rectB = tree.RectOf(b, Bounds)!.Value;
        var rectC = tree.RectOf(c, Bounds)!.Value;
        var rectA = tree.RectOf(a, Bounds)!.Value;

        Assert.Multiple(() =>
        {
            // Оставшиеся колонки делят освободившуюся треть поровну — по 0.5 каждой.
            Assert.That(rectB.X, Is.EqualTo(0).Within(1e-12), "полоса слева должна закрыться");
            Assert.That(rectB.W, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(rectB.H, Is.EqualTo(1.0).Within(1e-12));

            // Переехавший тайл занял нижнюю половину правой колонки.
            Assert.That(rectC, Is.EqualTo(new RectD(0.5, 0, 0.5, 0.5)));
            Assert.That(rectA, Is.EqualTo(new RectD(0.5, 0.5, 0.5, 0.5)));

            Assert.That(tree.Project().Sum(t => t.Rect.Area), Is.EqualTo(1.0).Within(1e-9));
        });

        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// Перенос последнего тайла к краю окна оставляет его на всей области.
    /// </summary>
    [Test]
    public void Move_SingleTileToWindowEdge_StaysFullArea()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(1);

        Assert.That(DropZoneResolver.Apply(tree, DropTarget.Dock(DockSide.Right), ids[0]), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.LeafCount, Is.EqualTo(1));
            Assert.That(tree.RectOf(ids[0], Bounds), Is.EqualTo(RectD.Unit));
        });
    }

    /// <summary>
    /// Перенос двух тайлов друг за другом не порождает вложенных
    /// одноориентированных сплитов и не оставляет вырожденных узлов.
    /// </summary>
    [Test]
    public void Move_RepeatedDockToSameSide_KeepsTreeFlat()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(4);

        foreach (var id in ids)
        {
            DropZoneResolver.Apply(tree, DropTarget.Dock(DockSide.Left), id);
            LayoutTestHelpers.AssertInvariants(tree, $"после дока {id}");
        }

        Assert.That(tree.LeafCount, Is.EqualTo(4));
    }

    /// <summary>
    /// Перенос тайла в дереве из двух листьев не создаёт пустого дерева
    /// в промежуточном состоянии.
    /// </summary>
    [Test]
    public void Move_TwoTiles_SurvivesInsertThenRemove()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(2);

        Assert.That(DropZoneResolver.Apply(tree, DropTarget.EdgeSplit(ids[1], DockSide.Top), ids[0]), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.LeafCount, Is.EqualTo(2));
            Assert.That(tree.RectOf(ids[0], Bounds), Is.EqualTo(new RectD(0, 0, 1, 0.5)));
            Assert.That(tree.RectOf(ids[1], Bounds), Is.EqualTo(new RectD(0, 0.5, 1, 0.5)));
        });
    }
}
