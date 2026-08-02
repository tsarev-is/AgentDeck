using AgentDeck.Layout;
using NUnit.Framework;

namespace AgentDeck.Tests.Layout;

/// <summary>
/// Hit-testing сплиттеров: рёбра, углы и внутренность тайла.
/// </summary>
[TestFixture]
public class SplitterHitTesterTests
{
    private static readonly RectD Bounds = new(0, 0, 1000, 800);

    /// <summary>
    /// Точка на общей границе 2-way сплита даёт верный узел и индекс.
    /// </summary>
    [Test]
    public void HitTest_SharedEdgeOfTwoWaySplit_ReturnsNodeAndIndex()
    {
        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
        ]));

        var grab = SplitterHitTester.HitTest(500, 400, tree, Bounds);

        Assert.Multiple(() =>
        {
            Assert.That(grab.Columns, Is.Not.Null, "вертикальная граница должна найтись");
            Assert.That(grab.Rows, Is.Null);
            Assert.That(grab.IsCorner, Is.False);
            Assert.That(grab.Columns!.Value.Node, Is.SameAs(tree.Root));
            Assert.That(grab.Columns.Value.Index, Is.Zero);
            Assert.That(grab.Columns.Value.Position, Is.EqualTo(500).Within(1e-9));
        });
    }

    /// <summary>
    /// Горизонтальная граница вертикального сплита ложится на ось Y.
    /// </summary>
    [Test]
    public void HitTest_HorizontalEdge_ReturnsRowHandle()
    {
        var tree = new LayoutTree(new SplitNode(Orientation.Vertical,
        [
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.25),
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.75),
        ]));

        var grab = SplitterHitTester.HitTest(500, 200, tree, Bounds);

        Assert.Multiple(() =>
        {
            Assert.That(grab.Rows, Is.Not.Null);
            Assert.That(grab.Columns, Is.Null);
            Assert.That(grab.Rows!.Value.Position, Is.EqualTo(200).Within(1e-9));
        });
    }

    /// <summary>
    /// Внутренность тайла границ не содержит.
    /// </summary>
    [Test]
    public void HitTest_TileInterior_ReturnsEmpty()
    {
        var (tree, _) = LayoutTestHelpers.BuildAuto(4);

        Assert.That(SplitterHitTester.HitTest(250, 200, tree, Bounds).IsEmpty, Is.True);
    }

    /// <summary>
    /// Угол между четырьмя тайлами даёт пару хендлов — тянутся обе оси.
    /// </summary>
    [Test]
    public void HitTest_CornerOfFourTiles_ReturnsBothAxes()
    {
        var (tree, _) = LayoutTestHelpers.BuildAuto(4);

        // Сетка 2×2: угол ровно в середине дека.
        var grab = SplitterHitTester.HitTest(500, 400, tree, Bounds);

        Assert.Multiple(() =>
        {
            Assert.That(grab.IsCorner, Is.True, "в углу должны найтись обе оси");
            Assert.That(grab.Columns!.Value.Node.Orientation, Is.EqualTo(Orientation.Horizontal));
            Assert.That(grab.Rows!.Value.Node.Orientation, Is.EqualTo(Orientation.Vertical));
        });
    }

    /// <summary>
    /// Граница вложенного сплита не ловится за пределами его поддерева,
    /// даже если координата совпадает.
    /// </summary>
    [Test]
    public void HitTest_NestedSplitEdge_IsLimitedToItsSubtree()
    {
        var nested = new SplitNode(Orientation.Vertical,
        [
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
        ]);

        // Левая половина — один тайл, правая поделена по горизонтали пополам.
        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
            new SplitChild(nested, 0.5),
        ]));

        Assert.Multiple(() =>
        {
            Assert.That(SplitterHitTester.HitTest(750, 400, tree, Bounds).Rows, Is.Not.Null, "внутри правой половины");
            Assert.That(SplitterHitTester.HitTest(250, 400, tree, Bounds).Rows, Is.Null, "слева границы нет");
        });
    }

    /// <summary>
    /// Полоса захвата симметрична и ограничена своей шириной.
    /// </summary>
    [TestCase(497.0, true)]
    [TestCase(503.0, true)]
    [TestCase(494.0, false)]
    [TestCase(506.0, false)]
    public void HitTest_GrabBandIsSymmetricAndBounded(double x, bool expected)
    {
        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
        ]));

        Assert.That(SplitterHitTester.HitTest(x, 400, tree, Bounds).Columns is not null, Is.EqualTo(expected));
    }

    /// <summary>
    /// В 3-way сплите ловится ближайшая из двух границ.
    /// </summary>
    [Test]
    public void HitTest_ThreeWaySplit_PicksNearestBoundary()
    {
        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(Guid.NewGuid()), 1.0 / 3),
            new SplitChild(new LeafNode(Guid.NewGuid()), 1.0 / 3),
            new SplitChild(new LeafNode(Guid.NewGuid()), 1.0 / 3),
        ]));

        Assert.Multiple(() =>
        {
            Assert.That(SplitterHitTester.HitTest(333.3, 400, tree, Bounds).Columns!.Value.Index, Is.Zero);
            Assert.That(SplitterHitTester.HitTest(666.7, 400, tree, Bounds).Columns!.Value.Index, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Позиция хендла пересчитывается по дереву и следует за ресайзом.
    /// </summary>
    [Test]
    public void PositionOf_FollowsResize()
    {
        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
        ]));

        var handle = SplitterHitTester.HitTest(500, 400, tree, Bounds).Columns!.Value;
        Assert.That(SplitterHitTester.PositionOf(handle, tree, Bounds), Is.EqualTo(500).Within(1e-9));

        tree.ResizeSplitter(handle.Node, handle.Index, 0.2);

        Assert.That(SplitterHitTester.PositionOf(handle, tree, Bounds), Is.EqualTo(700).Within(1e-9));
    }

    /// <summary>
    /// В дереве из одного тайла границ нет.
    /// </summary>
    [Test]
    public void HitTest_SingleTile_ReturnsEmpty()
    {
        var (tree, _) = LayoutTestHelpers.BuildAuto(1);

        Assert.That(SplitterHitTester.HitTest(500, 400, tree, Bounds).IsEmpty, Is.True);
    }
}
