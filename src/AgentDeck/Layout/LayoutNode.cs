namespace AgentDeck.Layout;

/// <summary>
/// Узел дерева раскладки: либо лист с тайлом, либо сплит с детьми.
/// </summary>
public abstract class LayoutNode
{
    /// <summary>
    /// Создаёт глубокую копию поддерева.
    /// </summary>
    public abstract LayoutNode Clone();

    /// <summary>
    /// Перечисляет все листья поддерева в порядке обхода в глубину.
    /// </summary>
    public IEnumerable<LeafNode> Leaves()
    {
        switch (this)
        {
            case LeafNode leaf:
                yield return leaf;
                break;

            case SplitNode split:
                foreach (var child in split.Children)
                {
                    foreach (var leaf in child.Node.Leaves())
                    {
                        yield return leaf;
                    }
                }

                break;
        }
    }
}

/// <summary>
/// Лист дерева — прямоугольник одного тайла.
/// </summary>
public sealed class LeafNode : LayoutNode
{
    /// <summary>
    /// Создаёт лист для указанного тайла.
    /// </summary>
    public LeafNode(Guid tileId)
    {
        TileId = tileId;
    }

    /// <summary>
    /// Идентификатор тайла. Меняется только при обмене местами (swap).
    /// </summary>
    public Guid TileId { get; set; }

    /// <inheritdoc />
    public override LayoutNode Clone() => new LeafNode(TileId);
}

/// <summary>
/// Ребёнок сплита вместе с его долей области родителя.
/// </summary>
/// <param name="Node">Поддерево ребёнка.</param>
/// <param name="Ratio">Доля области родителя вдоль оси сплита; сумма долей равна 1.</param>
public sealed record SplitChild(LayoutNode Node, double Ratio);

/// <summary>
/// Сплит — k-арное деление области вдоль одной оси.
/// </summary>
public sealed class SplitNode : LayoutNode
{
    /// <summary>
    /// Создаёт сплит с указанной ориентацией и детьми.
    /// </summary>
    public SplitNode(Orientation orientation, IEnumerable<SplitChild> children)
    {
        Orientation = orientation;
        Children = [.. children];
    }

    /// <summary>
    /// Ось, вдоль которой доли делят область узла.
    /// </summary>
    public Orientation Orientation { get; }

    /// <summary>
    /// Дети сплита в порядке слева направо (Horizontal) или сверху вниз (Vertical).
    /// </summary>
    public List<SplitChild> Children { get; }

    /// <inheritdoc />
    public override LayoutNode Clone()
        => new SplitNode(Orientation, Children.Select(c => new SplitChild(c.Node.Clone(), c.Ratio)));
}
