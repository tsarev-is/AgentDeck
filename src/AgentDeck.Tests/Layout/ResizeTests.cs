using AgentDeck.Layout;
using NUnit.Framework;

namespace AgentDeck.Tests.Layout;

/// <summary>
/// Ресайз сплиттеров: сдвиг общей границы, неизменность внешних границ, clamp по min-size.
/// </summary>
[TestFixture]
public class ResizeTests
{
    /// <summary>
    /// Сдвиг общей границы двигает только её: внешние грани соседей не меняются.
    /// </summary>
    [Test]
    public void ResizeSplitter_MovesSharedEdgeOnly()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 0.5),
            new SplitChild(new LeafNode(b), 0.5),
        ]));

        Assert.That(tree.ResizeSplitter((SplitNode)tree.Root!, 0, 0.1), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Rect(a), Is.EqualTo(new RectD(0, 0, 0.6, 1)));
            Assert.That(tree.Rect(b).X, Is.EqualTo(0.6).Within(1e-12));
            Assert.That(tree.Rect(b).Right, Is.EqualTo(1.0).Within(1e-12), "внешняя грань не должна двигаться");
        });

        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// В 3-way сплите двигается только выбранная граница, третий ребёнок не затронут.
    /// </summary>
    [Test]
    public void ResizeSplitter_ThreeWaySplit_TouchesOnlyAdjacentPair()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 1.0 / 3),
            new SplitChild(new LeafNode(b), 1.0 / 3),
            new SplitChild(new LeafNode(c), 1.0 / 3),
        ]));

        var thirdBefore = tree.Rect(c);

        Assert.That(tree.ResizeSplitter((SplitNode)tree.Root!, 0, 0.1), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Rect(a).W, Is.EqualTo(1.0 / 3 + 0.1).Within(1e-12));
            Assert.That(tree.Rect(b).W, Is.EqualTo(1.0 / 3 - 0.1).Within(1e-12));
            Assert.That(tree.Rect(c), Is.EqualTo(thirdBefore), "третий ребёнок не должен меняться");
        });

        Assert.That(((SplitNode)tree.Root!).Children.Sum(x => x.Ratio), Is.EqualTo(1.0).Within(1e-12));
    }

    /// <summary>
    /// Ресайз во вложенном сплите работает в локальных координатах узла:
    /// глобальная дельта пересчитывается через фактический размер узла.
    /// </summary>
    [Test]
    public void ResizeSplitter_NestedSplit_UsesNodeExtentForDelta()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        // Правая половина поделена по вертикали: её высота 1, ширина 0.5.
        var nested = new SplitNode(Orientation.Vertical,
        [
            new SplitChild(new LeafNode(b), 0.5),
            new SplitChild(new LeafNode(c), 0.5),
        ]);

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 0.5),
            new SplitChild(nested, 0.5),
        ]));

        Assert.That(tree.ResizeSplitter(nested, 0, 0.2), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Rect(b).H, Is.EqualTo(0.7).Within(1e-12));
            Assert.That(tree.Rect(c).H, Is.EqualTo(0.3).Within(1e-12));
            Assert.That(tree.Rect(a), Is.EqualTo(new RectD(0, 0, 0.5, 1)), "левая половина не затронута");
        });

        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// Пересчёт дельты учитывает размер узла: во вложенном сплите половинной ширины
    /// та же глобальная дельта даёт вдвое больший сдвиг доли.
    /// </summary>
    [Test]
    public void ResizeSplitter_HalfWidthNode_DoublesRatioDelta()
    {
        var a = Guid.NewGuid();

        var nested = new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
        ]);

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 0.5),
            new SplitChild(new SplitNode(Orientation.Vertical,
            [
                new SplitChild(nested, 0.5),
                new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
            ]), 0.5),
        ]));

        // Узел nested имеет ширину 0.5, значит глобальный сдвиг 0.1 = локальная доля 0.2.
        Assert.That(tree.ResizeSplitter(nested, 0, 0.1), Is.True);
        Assert.That(nested.Children[0].Ratio, Is.EqualTo(0.7).Within(1e-12));
    }

    /// <summary>
    /// Clamp по минимальной ширине: тайл не ужимается уже MinWidth.
    /// </summary>
    [Test]
    public void ResizeSplitter_ClampsAtMinWidth()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 0.5),
            new SplitChild(new LeafNode(b), 0.5),
        ]));

        Assert.That(tree.ResizeSplitter((SplitNode)tree.Root!, 0, 10.0), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Rect(b).W, Is.EqualTo(LayoutConstants.MinWidth).Within(1e-12));
            Assert.That(tree.Rect(a).W, Is.EqualTo(1 - LayoutConstants.MinWidth).Within(1e-12));
        });

        Assert.That(tree.MinSizeSatisfied(), Is.True);
    }

    /// <summary>
    /// Clamp симметричен: сдвиг в обратную сторону упирается в min-size первого тайла.
    /// </summary>
    [Test]
    public void ResizeSplitter_ClampsAtMinWidthInBothDirections()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 0.5),
            new SplitChild(new LeafNode(b), 0.5),
        ]));

        Assert.That(tree.ResizeSplitter((SplitNode)tree.Root!, 0, -10.0), Is.True);

        Assert.That(tree.Rect(a).W, Is.EqualTo(LayoutConstants.MinWidth).Within(1e-12));
    }

    /// <summary>
    /// Clamp учитывает вложенные тайлы: соседний сплит из двух колонок требует двух минимумов.
    /// </summary>
    [Test]
    public void ResizeSplitter_ClampRespectsNestedRequirements()
    {
        var a = Guid.NewGuid();

        var nested = new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
        ]);

        // Корень вертикальный, поэтому внутри него горизонтальный сплит не сливается.
        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 0.5),
            new SplitChild(new SplitNode(Orientation.Vertical,
            [
                new SplitChild(nested, 0.5),
                new SplitChild(new LeafNode(Guid.NewGuid()), 0.5),
            ]), 0.5),
        ]));

        tree.ResizeSplitter((SplitNode)tree.Root!, 0, 10.0);

        Assert.That(tree.MinSizeSatisfied(), Is.True, "вложенным колонкам должно хватить по MinWidth каждой");
        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// Ресайз с некорректным индексом — no-op.
    /// </summary>
    [Test]
    public void ResizeSplitter_InvalidIndex_ReturnsFalse()
    {
        var (tree, _) = LayoutTestHelpers.BuildAuto(2);
        var root = (SplitNode)tree.Root!;

        Assert.Multiple(() =>
        {
            Assert.That(tree.ResizeSplitter(root, -1, 0.1), Is.False);
            Assert.That(tree.ResizeSplitter(root, 1, 0.1), Is.False, "последний ребёнок не имеет границы справа");
        });
    }

    /// <summary>
    /// Серия ресайзов сохраняет сумму долей равной единице.
    /// </summary>
    [Test]
    public void ResizeSplitter_RepeatedDrags_KeepRatioSumAtOne()
    {
        var (tree, _) = LayoutTestHelpers.BuildAuto(6);
        var random = new Random(7);

        var splits = tree.EnumerateNodes(RectD.Unit).Select(n => n.Node).OfType<SplitNode>().ToList();

        for (var i = 0; i < 200; i++)
        {
            var split = splits[random.Next(splits.Count)];
            tree.ResizeSplitter(split, random.Next(split.Children.Count - 1), random.NextDouble() * 0.4 - 0.2);
        }

        foreach (var split in splits)
        {
            Assert.That(split.Children.Sum(c => c.Ratio), Is.EqualTo(1.0).Within(1e-9));
        }

        LayoutTestHelpers.AssertInvariants(tree);
        Assert.That(tree.MinSizeSatisfied(), Is.True);
    }
}
