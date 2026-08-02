using AgentDeck.Layout;
using NUnit.Framework;

namespace AgentDeck.Tests.Layout;

/// <summary>
/// Вставка по грани тайла и док к краю окна.
/// </summary>
[TestFixture]
public class InsertEdgeTests
{
    /// <summary>
    /// Грань вдоль оси родителя: новый лист становится сиблингом с верным индексом,
    /// доля цели делится пополам, остальные доли не трогаются.
    /// </summary>
    [Test]
    public void InsertAtEdge_AlongParentAxis_BecomesSiblingAtCorrectIndex()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 0.5),
            new SplitChild(new LeafNode(b), 0.5),
        ]));

        var inserted = Guid.NewGuid();
        Assert.That(tree.InsertAtEdge(a, DockSide.Right, inserted), Is.True);

        var split = tree.Root as SplitNode;
        Assert.That(split, Is.Not.Null, "корень должен остаться горизонтальным сплитом");
        Assert.That(split!.Children, Has.Count.EqualTo(3), "новый лист должен стать сиблингом, а не вложенным сплитом");

        Assert.Multiple(() =>
        {
            Assert.That(tree.Rect(a), Is.EqualTo(new RectD(0.0, 0, 0.25, 1)));
            Assert.That(tree.Rect(inserted), Is.EqualTo(new RectD(0.25, 0, 0.25, 1)));
            Assert.That(tree.Rect(b), Is.EqualTo(new RectD(0.5, 0, 0.5, 1)));
        });

        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// Вставка у левой грани ставит новый лист перед целью.
    /// </summary>
    [Test]
    public void InsertAtEdge_LeadingSide_PlacesNewTileBeforeTarget()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 0.5),
            new SplitChild(new LeafNode(b), 0.5),
        ]));

        var inserted = Guid.NewGuid();
        Assert.That(tree.InsertAtEdge(b, DockSide.Left, inserted), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Rect(inserted).X, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(tree.Rect(b).X, Is.EqualTo(0.75).Within(1e-12));
        });
    }

    /// <summary>
    /// Ортогональная грань оборачивает лист в новый сплит 50/50.
    /// </summary>
    [Test]
    public void InsertAtEdge_OrthogonalSide_WrapsLeafInFiftyFiftySplit()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(a), 0.5),
            new SplitChild(new LeafNode(b), 0.5),
        ]));

        var inserted = Guid.NewGuid();
        Assert.That(tree.InsertAtEdge(a, DockSide.Bottom, inserted), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Rect(a), Is.EqualTo(new RectD(0, 0, 0.5, 0.5)));
            Assert.That(tree.Rect(inserted), Is.EqualTo(new RectD(0, 0.5, 0.5, 0.5)));
            Assert.That(tree.Rect(b), Is.EqualTo(new RectD(0.5, 0, 0.5, 1)), "сосед не должен меняться");
        });

        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// Вставка в единственный лист оборачивает корень.
    /// </summary>
    [Test]
    public void InsertAtEdge_IntoRootLeaf_WrapsRoot()
    {
        var a = Guid.NewGuid();
        var tree = new LayoutTree(new LeafNode(a));

        var inserted = Guid.NewGuid();
        Assert.That(tree.InsertAtEdge(a, DockSide.Top, inserted), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Rect(inserted), Is.EqualTo(new RectD(0, 0, 1, 0.5)));
            Assert.That(tree.Rect(a), Is.EqualTo(new RectD(0, 0.5, 1, 0.5)));
        });
    }

    /// <summary>
    /// Док слева от корня — полоса во всю высоту, остальные ужимаются пропорционально.
    /// </summary>
    [Test]
    public void InsertAtRootEdge_Left_CreatesFullHeightStrip()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(3);
        var before = ids.ToDictionary(id => id, id => tree.Rect(id));

        var docked = Guid.NewGuid();
        Assert.That(tree.InsertAtRootEdge(DockSide.Left, docked), Is.True);

        var strip = tree.Rect(docked);

        Assert.Multiple(() =>
        {
            Assert.That(strip.X, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(strip.Y, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(strip.H, Is.EqualTo(1.0).Within(1e-12), "док слева должен занимать всю высоту");
            Assert.That(strip.W, Is.EqualTo(0.25).Within(1e-12), "четвёртому тайлу достаётся 1/4");
        });

        // Остальные сохранили относительные пропорции и сдвинулись вправо на ширину полосы.
        var scale = 1 - strip.W;
        foreach (var id in ids)
        {
            Assert.That(tree.Rect(id).W, Is.EqualTo(before[id].W * scale).Within(1e-12), $"пропорция тайла {id}");
            Assert.That(tree.Rect(id).X, Is.EqualTo(strip.W + before[id].X * scale).Within(1e-12));
            Assert.That(tree.Rect(id).H, Is.EqualTo(before[id].H).Within(1e-12), "высоты не должны меняться");
        }

        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// Док сверху — полоса во всю ширину.
    /// </summary>
    [Test]
    public void InsertAtRootEdge_Top_CreatesFullWidthStrip()
    {
        var (tree, _) = LayoutTestHelpers.BuildAuto(2);

        var docked = Guid.NewGuid();
        Assert.That(tree.InsertAtRootEdge(DockSide.Top, docked), Is.True);

        var strip = tree.Rect(docked);

        Assert.Multiple(() =>
        {
            Assert.That(strip.X, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(strip.Y, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(strip.W, Is.EqualTo(1.0).Within(1e-12));
        });

        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// Док в пустое дерево создаёт единственный тайл на всю область.
    /// </summary>
    [Test]
    public void InsertAtRootEdge_EmptyTree_CreatesSingleTile()
    {
        var tree = new LayoutTree();
        var id = Guid.NewGuid();

        Assert.That(tree.InsertAtRootEdge(DockSide.Right, id), Is.True);
        Assert.That(tree.Rect(id), Is.EqualTo(RectD.Unit));
    }

    /// <summary>
    /// Повторный док одной стороной подряд не создаёт вложенных одноориентированных сплитов.
    /// </summary>
    [Test]
    public void InsertAtRootEdge_RepeatedSameSide_StaysFlat()
    {
        var tree = new LayoutTree();
        tree.AddTileAuto(Guid.NewGuid());

        for (var i = 0; i < 5; i++)
        {
            Assert.That(tree.InsertAtRootEdge(DockSide.Left, Guid.NewGuid()), Is.True);
            LayoutTestHelpers.AssertInvariants(tree, $"после {i + 2} тайлов");
        }

        Assert.That(tree.Root, Is.InstanceOf<SplitNode>());
        Assert.That(((SplitNode)tree.Root!).Children, Has.Count.EqualTo(6), "все доки должны лечь в один сплит");
    }

    /// <summary>
    /// CanInsertAtEdge запрещает вставку, которая делает тайл уже минимальной ширины.
    /// </summary>
    [Test]
    public void CanInsertAtEdge_ReturnsFalse_WhenMinWidthWouldBeViolated()
    {
        var target = Guid.NewGuid();

        // Целевой тайл шириной чуть больше одного минимума — половина уже недопустима.
        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(target), 0.2),
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.8),
        ]));

        Assert.Multiple(() =>
        {
            Assert.That(tree.CanInsertAtEdge(target, DockSide.Right), Is.False, "0.2 / 2 = 0.1 < MinWidth 0.14");
            Assert.That(tree.CanInsertAtEdge(target, DockSide.Bottom), Is.True, "по вертикали места хватает");
        });
    }

    /// <summary>
    /// CanInsertAtEdge запрещает вставку, которая делает тайл ниже минимальной высоты.
    /// </summary>
    [Test]
    public void CanInsertAtEdge_ReturnsFalse_WhenMinHeightWouldBeViolated()
    {
        var target = Guid.NewGuid();

        var tree = new LayoutTree(new SplitNode(Orientation.Vertical,
        [
            new SplitChild(new LeafNode(target), 0.2),
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.8),
        ]));

        Assert.That(tree.CanInsertAtEdge(target, DockSide.Bottom), Is.False, "0.2 / 2 = 0.1 < MinHeight 0.12");
    }

    /// <summary>
    /// Проверка вставки не изменяет исходное дерево.
    /// </summary>
    [Test]
    public void CanInsertAtEdge_DoesNotMutateTree()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(3);
        var before = LayoutSerializer.Serialize(tree);

        tree.CanInsertAtEdge(ids[0], DockSide.Right);
        tree.CanInsertAtRootEdge(DockSide.Bottom);

        Assert.That(LayoutSerializer.Serialize(tree), Is.EqualTo(before));
    }

    /// <summary>
    /// Перенос существующего тайла учитывает его удаление со старого места:
    /// в переполненной раскладке вставка допустима именно потому, что тайл уезжает.
    /// </summary>
    [Test]
    public void CanInsertAtEdge_WithMovingTile_AccountsForRemoval()
    {
        var target = Guid.NewGuid();
        var moving = Guid.NewGuid();

        // Полоса цели 0.24: без учёта переезда половина 0.12 нарушила бы MinWidth,
        // но moving освобождает свои 0.52, и после нормализации места хватает.
        var tree = new LayoutTree(new SplitNode(Orientation.Horizontal,
        [
            new SplitChild(new LeafNode(target), 0.24),
            new SplitChild(new LeafNode(moving), 0.52),
            new SplitChild(new LeafNode(Guid.NewGuid()), 0.24),
        ]));

        Assert.Multiple(() =>
        {
            Assert.That(tree.CanInsertAtEdge(target, DockSide.Right), Is.False, "без учёта переезда места нет");
            Assert.That(tree.CanInsertAtEdge(target, DockSide.Right, moving), Is.True, "с учётом переезда место есть");
        });
    }

    /// <summary>
    /// Тайл нельзя вставить к грани самого себя.
    /// </summary>
    [Test]
    public void CanInsertAtEdge_MovingOntoItself_ReturnsFalse()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(2);

        Assert.That(tree.CanInsertAtEdge(ids[0], DockSide.Right, ids[0]), Is.False);
    }
}
