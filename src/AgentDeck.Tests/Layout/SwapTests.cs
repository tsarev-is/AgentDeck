using AgentDeck.Layout;
using NUnit.Framework;

namespace AgentDeck.Tests.Layout;

/// <summary>
/// Обмен тайлов местами.
/// </summary>
[TestFixture]
public class SwapTests
{
    /// <summary>
    /// Прямоугольники обмениваются точно, форма дерева и доли не меняются.
    /// </summary>
    [Test]
    public void Swap_ExchangesRectsExactly_AndKeepsTreeShape()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(5);

        var shapeBefore = Shape(tree.Root!);
        var rectA = tree.Rect(ids[0]);
        var rectB = tree.Rect(ids[3]);
        Assert.That(rectA, Is.Not.EqualTo(rectB), "для осмысленного теста прямоугольники должны различаться");

        Assert.That(tree.Swap(ids[0], ids[3]), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Rect(ids[0]), Is.EqualTo(rectB));
            Assert.That(tree.Rect(ids[3]), Is.EqualTo(rectA));
            Assert.That(Shape(tree.Root!), Is.EqualTo(shapeBefore), "форма дерева и доли не должны меняться");
        });

        LayoutTestHelpers.AssertInvariants(tree);
    }

    /// <summary>
    /// Прочие тайлы после обмена остаются на своих местах.
    /// </summary>
    [Test]
    public void Swap_LeavesOtherTilesUntouched()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(4);
        var untouched = ids.Except([ids[0], ids[2]]).ToDictionary(id => id, id => tree.Rect(id));

        tree.Swap(ids[0], ids[2]);

        foreach (var (id, rect) in untouched)
        {
            Assert.That(tree.Rect(id), Is.EqualTo(rect));
        }
    }

    /// <summary>
    /// Двойной обмен возвращает исходную раскладку.
    /// </summary>
    [Test]
    public void Swap_TwiceRestoresOriginal()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(3);
        var before = LayoutSerializer.Serialize(tree);

        tree.Swap(ids[0], ids[2]);
        tree.Swap(ids[0], ids[2]);

        Assert.That(LayoutSerializer.Serialize(tree), Is.EqualTo(before));
    }

    /// <summary>
    /// Обмен с самим собой и с отсутствующим тайлом — no-op.
    /// </summary>
    [Test]
    public void Swap_SelfOrUnknown_ReturnsFalse()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(2);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Swap(ids[0], ids[0]), Is.False);
            Assert.That(tree.Swap(ids[0], Guid.NewGuid()), Is.False);
        });
    }

    /// <summary>
    /// Строит структурный отпечаток дерева без идентификаторов тайлов.
    /// </summary>
    private static string Shape(LayoutNode node) => node switch
    {
        LeafNode => "L",
        SplitNode split =>
            $"({split.Orientation}:{string.Join(",", split.Children.Select(c => $"{c.Ratio:F12}{Shape(c.Node)}"))})",
        _ => "?",
    };
}
