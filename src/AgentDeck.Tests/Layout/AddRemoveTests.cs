using AgentDeck.Layout;
using NUnit.Framework;

namespace AgentDeck.Tests.Layout;

/// <summary>
/// Добавление и удаление тайлов: количество листьев, поглощение места сиблингами,
/// схлопывание вырожденных узлов.
/// </summary>
[TestFixture]
public class AddRemoveTests
{
    /// <summary>
    /// Автодобавление 1..8 тайлов держит инварианты и даёт ровно N листьев.
    /// </summary>
    [Test]
    public void AddTileAuto_UpToEight_KeepsInvariants()
    {
        var tree = new LayoutTree();

        for (var count = 1; count <= LayoutConstants.MaxTiles; count++)
        {
            Assert.That(tree.AddTileAuto(Guid.NewGuid()), Is.True, $"добавление тайла #{count}");
            Assert.That(tree.LeafCount, Is.EqualTo(count));
            Assert.That(tree.MinSizeSatisfied(), Is.True, $"минимальный размер нарушен на {count} тайлах");
            LayoutTestHelpers.AssertInvariants(tree, $"{count} тайлов");
        }
    }

    /// <summary>
    /// Единственный тайл занимает всю область.
    /// </summary>
    [Test]
    public void AddTileAuto_First_FillsWholeArea()
    {
        var tree = new LayoutTree();
        var id = Guid.NewGuid();
        tree.AddTileAuto(id);

        Assert.That(tree.Rect(id), Is.EqualTo(RectD.Unit));
    }

    /// <summary>
    /// Второй тайл делит область пополам вдоль длинной оси — по вертикали пополам по ширине.
    /// </summary>
    [Test]
    public void AddTileAuto_Second_SplitsAlongLongAxis()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(2);

        var first = tree.Rect(ids[0]);
        var second = tree.Rect(ids[1]);

        Assert.Multiple(() =>
        {
            Assert.That(first.W, Is.EqualTo(0.5).Within(1e-12), "исходный тайл ужался по ширине");
            Assert.That(second.W, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(first.H, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(second.H, Is.EqualTo(1.0).Within(1e-12));
        });
    }

    /// <summary>
    /// Добавление сверх восьми не запрещено движком — кап живёт во ViewModel;
    /// движок лишь отказывает, когда место кончилось физически.
    /// </summary>
    [Test]
    public void AddTileAuto_DuplicateId_IsRejected()
    {
        var tree = new LayoutTree();
        var id = Guid.NewGuid();

        Assert.That(tree.AddTileAuto(id), Is.True);
        Assert.That(tree.AddTileAuto(id), Is.False);
        Assert.That(tree.LeafCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Удаление среднего ребёнка 3-way сплита: сиблинги поглощают его долю пропорционально.
    /// </summary>
    [Test]
    public void Remove_MiddleChildOfThreeWaySplit_SiblingsAbsorbProportionally()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 0.2),
            new SplitChild(new LeafNode(b), 0.5),
            new SplitChild(new LeafNode(c), 0.3),
        ]));

        Assert.That(tree.Remove(b), Is.True);

        // Доли 0.2 и 0.3 нормируются к 0.4 и 0.6.
        Assert.Multiple(() =>
        {
            Assert.That(tree.Rect(a).W, Is.EqualTo(0.4).Within(1e-12));
            Assert.That(tree.Rect(c).W, Is.EqualTo(0.6).Within(1e-12));
            Assert.That(tree.Rect(a).X, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(tree.Rect(c).X, Is.EqualTo(0.4).Within(1e-12));
        });

        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// Удаление до одного ребёнка схлопывает сплит: оставшийся тайл занимает всю область родителя.
    /// </summary>
    [Test]
    public void Remove_DownToSingleChild_FlattensSplit()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(2);

        Assert.That(tree.Remove(ids[0]), Is.True);

        Assert.That(tree.Root, Is.InstanceOf<LeafNode>(), "сплит с одним ребёнком не схлопнулся");
        Assert.That(tree.Rect(ids[1]), Is.EqualTo(RectD.Unit));
        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// Удаление вложенного сплита схлопывает цепочку и сливает одноориентированные узлы.
    /// </summary>
    [Test]
    public void Remove_CollapsesNestedSplitsWithoutSameOrientationNesting()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(4);

        Assert.That(tree.Remove(ids[3]), Is.True);
        LayoutTestHelpers.AssertInvariants(tree, "после удаления 4-го");

        Assert.That(tree.Remove(ids[1]), Is.True);
        LayoutTestHelpers.AssertInvariants(tree, "после удаления 2-го");

        Assert.That(tree.LeafCount, Is.EqualTo(2));
    }

    /// <summary>
    /// Удаление последнего тайла даёт пустое дерево.
    /// </summary>
    [Test]
    public void Remove_LastTile_EmptiesTree()
    {
        var tree = new LayoutTree();
        var id = Guid.NewGuid();
        tree.AddTileAuto(id);

        Assert.That(tree.Remove(id), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(tree.Root, Is.Null);
            Assert.That(tree.LeafCount, Is.Zero);
            Assert.That(tree.Project(), Is.Empty);
        });
    }

    /// <summary>
    /// Удаление отсутствующего тайла — no-op.
    /// </summary>
    [Test]
    public void Remove_UnknownTile_ReturnsFalse()
    {
        var (tree, _) = LayoutTestHelpers.BuildAuto(3);

        Assert.That(tree.Remove(Guid.NewGuid()), Is.False);
        Assert.That(tree.LeafCount, Is.EqualTo(3));
    }

    /// <summary>
    /// Удаление всех тайлов по одному не оставляет мусора в дереве.
    /// </summary>
    [Test]
    public void Remove_AllTilesOneByOne_EndsEmpty()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(LayoutConstants.MaxTiles);

        foreach (var id in ids)
        {
            Assert.That(tree.Remove(id), Is.True);
            LayoutTestHelpers.AssertInvariants(tree, $"после удаления {id}");
        }

        Assert.That(tree.Root, Is.Null);
    }
}
