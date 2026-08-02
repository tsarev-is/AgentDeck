using AgentDeck.Layout;
using NUnit.Framework;

namespace AgentDeck.Tests.Layout;

/// <summary>
/// Property-style проверка: длинные случайные последовательности операций
/// не должны нарушать ни один инвариант раскладки.
/// </summary>
[TestFixture]
public class LayoutInvariantTests
{
    /// <summary>
    /// Тысячи случайных операций add/insert/dock/remove/swap/resize с фиксированным
    /// seed: проекция всегда покрывает область целиком, без пересечений и дыр,
    /// доли каждого сплита суммируются в единицу, вырожденных узлов нет.
    /// </summary>
    [TestCase(1)]
    [TestCase(20240607)]
    [TestCase(int.MaxValue / 3)]
    public void RandomOperationSequence_KeepsAllInvariants(int seed)
    {
        var random = new Random(seed);
        var tree = new LayoutTree();
        var ids = new List<Guid>();

        for (var step = 0; step < 1200; step++)
        {
            var operation = random.Next(6);
            var context = $"seed={seed} step={step} op={operation} tiles={ids.Count}";

            switch (operation)
            {
                case 0:
                    AddAuto(tree, ids, random);
                    break;

                case 1:
                    InsertAtEdge(tree, ids, random);
                    break;

                case 2:
                    Dock(tree, ids, random);
                    break;

                case 3:
                    Remove(tree, ids, random);
                    break;

                case 4:
                    Swap(tree, ids, random);
                    break;

                default:
                    Resize(tree, random);
                    break;
            }

            LayoutTestHelpers.AssertInvariants(tree, context);

            // Списки тайлов ViewModel и дерева обязаны совпадать после любой операции.
            Assert.That(tree.TileIds.OrderBy(x => x), Is.EqualTo(ids.OrderBy(x => x)), $"расхождение состава тайлов [{context}]");
            Assert.That(tree.MinSizeSatisfied(), Is.True, $"нарушен минимальный размер [{context}]");
        }

        Assert.That(ids, Is.Not.Empty, "последовательность должна оставить хотя бы один тайл");
    }

    /// <summary>
    /// Каждое промежуточное состояние переживает round-trip сериализации без потерь.
    /// </summary>
    [Test]
    public void RandomOperationSequence_SurvivesSerializationRoundTrip()
    {
        var random = new Random(99);
        var tree = new LayoutTree();
        var ids = new List<Guid>();

        for (var step = 0; step < 300; step++)
        {
            switch (random.Next(4))
            {
                case 0:
                    AddAuto(tree, ids, random);
                    break;

                case 1:
                    InsertAtEdge(tree, ids, random);
                    break;

                case 2:
                    Remove(tree, ids, random);
                    break;

                default:
                    Resize(tree, random);
                    break;
            }

            var restored = LayoutSerializer.Deserialize(LayoutSerializer.Serialize(tree));
            Assert.That(restored, Is.Not.Null, $"round-trip провалился на шаге {step}");
            Assert.That(restored!.Project(), Is.EqualTo(tree.Project()), $"проекция разошлась на шаге {step}");
        }
    }

    private static void AddAuto(LayoutTree tree, List<Guid> ids, Random random)
    {
        if (ids.Count >= LayoutConstants.MaxTiles)
        {
            return;
        }

        var id = Guid.NewGuid();
        if (tree.AddTileAuto(id))
        {
            ids.Add(id);
        }
    }

    private static void InsertAtEdge(LayoutTree tree, List<Guid> ids, Random random)
    {
        if (ids.Count is 0 or >= LayoutConstants.MaxTiles)
        {
            return;
        }

        var target = ids[random.Next(ids.Count)];
        var side = (DockSide)random.Next(4);
        if (!tree.CanInsertAtEdge(target, side))
        {
            return;
        }

        var id = Guid.NewGuid();
        if (tree.InsertAtEdge(target, side, id))
        {
            ids.Add(id);
        }
    }

    private static void Dock(LayoutTree tree, List<Guid> ids, Random random)
    {
        if (ids.Count >= LayoutConstants.MaxTiles)
        {
            return;
        }

        var side = (DockSide)random.Next(4);
        if (!tree.CanInsertAtRootEdge(side))
        {
            return;
        }

        var id = Guid.NewGuid();
        if (tree.InsertAtRootEdge(side, id))
        {
            ids.Add(id);
        }
    }

    private static void Remove(LayoutTree tree, List<Guid> ids, Random random)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var index = random.Next(ids.Count);
        Assert.That(tree.Remove(ids[index]), Is.True, "удаление известного тайла не должно падать");
        ids.RemoveAt(index);
    }

    private static void Swap(LayoutTree tree, List<Guid> ids, Random random)
    {
        if (ids.Count < 2)
        {
            return;
        }

        var first = ids[random.Next(ids.Count)];
        var second = ids[random.Next(ids.Count)];
        tree.Swap(first, second);
    }

    private static void Resize(LayoutTree tree, Random random)
    {
        var splits = tree.EnumerateNodes(RectD.Unit).Select(n => n.Node).OfType<SplitNode>().ToList();
        if (splits.Count == 0)
        {
            return;
        }

        var split = splits[random.Next(splits.Count)];
        tree.ResizeSplitter(split, random.Next(split.Children.Count - 1), random.NextDouble() * 0.6 - 0.3);
    }
}
