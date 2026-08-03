namespace AgentDeck.Layout;

/// <summary>
/// Прямоугольник конкретного тайла в проекции дерева.
/// </summary>
/// <param name="TileId">
/// Идентификатор тайла.
/// </param>
/// <param name="Rect">
/// Область тайла.
/// </param>
public readonly record struct TileRect(Guid TileId, RectD Rect);

/// <summary>
/// Дерево сплитов — единственный источник геометрии дека.
/// Все операции сохраняют инварианты: полное покрытие области без пересечений,
/// сумма долей каждого сплита равна 1, нет сплитов с одним ребёнком и нет
/// вложенных сплитов одинаковой ориентации.
/// </summary>
public sealed class LayoutTree
{
    /// <summary>
    /// Создаёт пустое дерево.
    /// </summary>
    public LayoutTree()
    {
    }

    /// <summary>
    /// Создаёт дерево с указанным корнем и приводит его к нормальной форме.
    /// </summary>
    public LayoutTree(LayoutNode? root)
    {
        Root = root;
        Normalize();
    }

    /// <summary>
    /// Корень дерева; null — пустой дек.
    /// </summary>
    public LayoutNode? Root { get; private set; }

    /// <summary>
    /// Число тайлов в дереве.
    /// </summary>
    public int LeafCount => Root?.Leaves().Count() ?? 0;

    /// <summary>
    /// Идентификаторы всех тайлов в порядке обхода в глубину.
    /// </summary>
    public IEnumerable<Guid> TileIds => Root is null ? [] : Root.Leaves().Select(l => l.TileId);

    /// <summary>
    /// Проверяет наличие тайла в дереве.
    /// </summary>
    public bool Contains(Guid tileId) => TileIds.Contains(tileId);

    /// <summary>
    /// Создаёт глубокую копию дерева.
    /// </summary>
    public LayoutTree Clone() => new(Root?.Clone());

    // ─────────────────────────── Проекция ───────────────────────────

    /// <summary>
    /// Проецирует дерево на указанную область: чистая функция, единственный
    /// источник геометрии для рендера и hit-testing.
    /// </summary>
    public List<TileRect> Project(RectD bounds)
    {
        var result = new List<TileRect>();
        if (Root is not null)
        {
            ProjectNode(Root, bounds, result);
        }

        return result;
    }

    /// <summary>
    /// Проецирует дерево на единичную область.
    /// </summary>
    public List<TileRect> Project() => Project(RectD.Unit);

    /// <summary>
    /// Возвращает область тайла в указанных границах либо null, если тайла нет.
    /// </summary>
    public RectD? RectOf(Guid tileId, RectD bounds)
    {
        foreach (var tile in Project(bounds))
        {
            if (tile.TileId == tileId)
            {
                return tile.Rect;
            }
        }

        return null;
    }

    /// <summary>
    /// Перечисляет все узлы дерева вместе с их областями.
    /// </summary>
    public IEnumerable<(LayoutNode Node, RectD Rect)> EnumerateNodes(RectD bounds)
    {
        if (Root is null)
        {
            yield break;
        }

        var stack = new Stack<(LayoutNode Node, RectD Rect)>();
        stack.Push((Root, bounds));

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            if (current.Node is SplitNode split)
            {
                foreach (var (child, childRect) in ChildRects(split, current.Rect))
                {
                    stack.Push((child.Node, childRect));
                }
            }
        }
    }

    // ─────────────────────────── Мутации ───────────────────────────

    /// <summary>
    /// Добавляет тайл автоматически: сплитит наибольший по площади лист вдоль
    /// его длинной оси пополам. Возвращает false, если места не нашлось.
    /// </summary>
    public bool AddTileAuto(Guid tileId)
    {
        if (Contains(tileId))
        {
            return false;
        }

        if (Root is null)
        {
            Root = new LeafNode(tileId);
            return true;
        }

        var candidates = Project().OrderByDescending(t => t.Rect.Area).ToList();

        foreach (var candidate in candidates)
        {
            var sides = candidate.Rect.W >= candidate.Rect.H
                ? new[] { DockSide.Right, DockSide.Bottom }
                : new[] { DockSide.Bottom, DockSide.Right };

            foreach (var side in sides)
            {
                if (CanInsertAtEdge(candidate.TileId, side))
                {
                    return InsertAtEdge(candidate.TileId, side, tileId);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Вставляет новый тайл у указанной грани целевого тайла: сиблингом, если ось
    /// грани совпадает с ориентацией родителя, иначе оборачивает лист в ортогональный сплит.
    /// </summary>
    public bool InsertAtEdge(Guid targetTileId, DockSide side, Guid newTileId)
    {
        if (Contains(newTileId) || !TryFind(targetTileId, out var leaf, out var parent, out var index))
        {
            return false;
        }

        var axis = side.Axis();
        var newLeaf = new LeafNode(newTileId);

        if (parent is not null && parent.Orientation == axis)
        {
            var half = parent.Children[index].Ratio / 2.0;
            parent.Children[index] = parent.Children[index] with { Ratio = half };
            parent.Children.Insert(side.IsLeading() ? index : index + 1, new SplitChild(newLeaf, half));
        }
        else
        {
            var children = side.IsLeading()
                ? new[] { new SplitChild(newLeaf, 0.5), new SplitChild(leaf, 0.5) }
                : new[] { new SplitChild(leaf, 0.5), new SplitChild(newLeaf, 0.5) };

            var split = new SplitNode(axis, children);

            if (parent is null)
            {
                Root = split;
            }
            else
            {
                parent.Children[index] = parent.Children[index] with { Node = split };
            }
        }

        Normalize();
        return true;
    }

    /// <summary>
    /// Пристыковывает новый тайл к краю окна: полоса во всю высоту (слева/справа)
    /// или во всю ширину (сверху/снизу); остальные ужимаются пропорционально.
    /// </summary>
    public bool InsertAtRootEdge(DockSide side, Guid newTileId)
    {
        if (Contains(newTileId))
        {
            return false;
        }

        var newLeaf = new LeafNode(newTileId);

        if (Root is null)
        {
            Root = newLeaf;
            return true;
        }

        var axis = side.Axis();
        var share = Math.Max(1.0 / (LeafCount + 1), LayoutConstants.MinExtent(axis));

        if (Root is SplitNode rootSplit && rootSplit.Orientation == axis)
        {
            var scale = 1.0 - share;
            for (var i = 0; i < rootSplit.Children.Count; i++)
            {
                rootSplit.Children[i] = rootSplit.Children[i] with { Ratio = rootSplit.Children[i].Ratio * scale };
            }

            rootSplit.Children.Insert(side.IsLeading() ? 0 : rootSplit.Children.Count, new SplitChild(newLeaf, share));
        }
        else
        {
            var children = side.IsLeading()
                ? new[] { new SplitChild(newLeaf, share), new SplitChild(Root, 1.0 - share) }
                : new[] { new SplitChild(Root, 1.0 - share), new SplitChild(newLeaf, share) };

            Root = new SplitNode(axis, children);
        }

        Normalize();
        return true;
    }

    /// <summary>
    /// Переименовывает лист. Нужен переносу тайла: новое место сначала занимает
    /// временный лист, а после удаления старого получает исходный идентификатор.
    /// </summary>
    public bool Rename(Guid from, Guid to)
    {
        if (from == to || Contains(to) || !TryFind(from, out var leaf, out _, out _))
        {
            return false;
        }

        leaf.TileId = to;
        return true;
    }

    /// <summary>
    /// Меняет местами два тайла: форма дерева и все доли остаются прежними.
    /// </summary>
    public bool Swap(Guid first, Guid second)
    {
        if (first == second
            || !TryFind(first, out var firstLeaf, out _, out _)
            || !TryFind(second, out var secondLeaf, out _, out _))
        {
            return false;
        }

        (firstLeaf.TileId, secondLeaf.TileId) = (secondLeaf.TileId, firstLeaf.TileId);
        return true;
    }

    /// <summary>
    /// Удаляет тайл; его доля пропорционально раздаётся сиблингам, а вырожденные
    /// узлы схлопываются. Дыра в раскладке невозможна по построению.
    /// </summary>
    public bool Remove(Guid tileId)
    {
        if (!TryFind(tileId, out _, out var parent, out var index))
        {
            return false;
        }

        if (parent is null)
        {
            Root = null;
            return true;
        }

        parent.Children.RemoveAt(index);
        Normalize();
        return true;
    }

    /// <summary>
    /// Двигает границу между детьми <paramref name="index"/> и <paramref name="index"/> + 1
    /// на <paramref name="globalDelta"/> в нормированных координатах дека, с clamp по минимальному размеру.
    /// </summary>
    public bool ResizeSplitter(SplitNode node, int index, double globalDelta)
    {
        if (index < 0 || index + 1 >= node.Children.Count)
        {
            return false;
        }

        var nodeRect = FindNodeRect(node);
        if (nodeRect is null)
        {
            return false;
        }

        var extent = node.Orientation == Orientation.Horizontal ? nodeRect.Value.W : nodeRect.Value.H;
        if (extent <= LayoutConstants.Epsilon)
        {
            return false;
        }

        var first = node.Children[index];
        var second = node.Children[index + 1];
        var total = first.Ratio + second.Ratio;

        var minFirst = RequiredExtent(first.Node, node.Orientation) / extent;
        var minSecond = RequiredExtent(second.Node, node.Orientation) / extent;
        if (minFirst + minSecond > total + LayoutConstants.Epsilon)
        {
            return false;
        }

        var target = Math.Clamp(first.Ratio + globalDelta / extent, minFirst, total - minSecond);

        node.Children[index] = first with { Ratio = target };
        node.Children[index + 1] = second with { Ratio = total - target };
        return true;
    }

    // ─────────────────────────── Проверки ───────────────────────────

    /// <summary>
    /// Проверяет, что вставка у грани тайла не нарушит минимальный размер.
    /// <paramref name="movingTileId"/> задаёт тайл, который будет удалён со старого места
    /// (перенос = insert + remove).
    /// </summary>
    public bool CanInsertAtEdge(Guid targetTileId, DockSide side, Guid? movingTileId = null)
    {
        if (movingTileId == targetTileId)
        {
            return false;
        }

        var probe = Clone();
        if (!probe.InsertAtEdge(targetTileId, side, Guid.NewGuid()))
        {
            return false;
        }

        if (movingTileId.HasValue && !probe.Remove(movingTileId.Value))
        {
            return false;
        }

        return probe.MinSizeSatisfied();
    }

    /// <summary>
    /// Проверяет, что док к краю окна не нарушит минимальный размер.
    /// </summary>
    public bool CanInsertAtRootEdge(DockSide side, Guid? movingTileId = null)
    {
        var probe = Clone();
        if (!probe.InsertAtRootEdge(side, Guid.NewGuid()))
        {
            return false;
        }

        if (movingTileId.HasValue && !probe.Remove(movingTileId.Value))
        {
            return false;
        }

        return probe.MinSizeSatisfied();
    }

    /// <summary>
    /// Проверяет, что все тайлы не мельче минимального размера.
    /// </summary>
    public bool MinSizeSatisfied()
        => Project().All(t => t.Rect.W >= LayoutConstants.MinWidth - 1e-9
                              && t.Rect.H >= LayoutConstants.MinHeight - 1e-9);

    /// <summary>
    /// Проверяет структурные инварианты дерева.
    /// </summary>
    public bool Validate()
    {
        if (Root is null)
        {
            return true;
        }

        var ids = TileIds.ToList();
        return ids.Distinct().Count() == ids.Count && ValidateNode(Root, null);
    }

    /// <summary>
    /// Приводит дерево к нормальной форме: удаляет пустые узлы, схлопывает сплиты
    /// с одним ребёнком, сливает вложенные сплиты одинаковой ориентации и
    /// нормирует суммы долей к единице.
    /// </summary>
    public void Normalize() => Root = NormalizeNode(Root);

    // ─────────────────────────── Внутреннее ───────────────────────────

    /// <summary>
    /// Возвращает минимальный размер поддерева вдоль указанной оси при неизменных
    /// внутренних долях: ресайз двигает только выбранную границу, вложенные доли
    /// остаются прежними, поэтому доля каждого ребёнка задаёт свой нижний предел
    /// на размер родителя.
    /// </summary>
    internal static double RequiredExtent(LayoutNode node, Orientation axis)
    {
        if (node is not SplitNode split)
        {
            return LayoutConstants.MinExtent(axis);
        }

        return split.Orientation == axis
            ? split.Children.Max(c => RequiredExtent(c.Node, axis) / c.Ratio)
            : split.Children.Max(c => RequiredExtent(c.Node, axis));
    }

    /// <summary>
    /// Разбивает область сплита между его детьми; последняя грань точно совпадает
    /// с гранью родителя, что исключает дрейф координат.
    /// </summary>
    internal static IEnumerable<(SplitChild Child, RectD Rect)> ChildRects(SplitNode split, RectD rect)
    {
        var offset = 0.0;

        for (var i = 0; i < split.Children.Count; i++)
        {
            var child = split.Children[i];
            var start = offset;
            var end = i == split.Children.Count - 1 ? 1.0 : Math.Min(1.0, offset + child.Ratio);
            offset = end;

            yield return (child, split.Orientation == Orientation.Horizontal
                ? new RectD(rect.X + start * rect.W, rect.Y, (end - start) * rect.W, rect.H)
                : new RectD(rect.X, rect.Y + start * rect.H, rect.W, (end - start) * rect.H));
        }
    }

    private static void ProjectNode(LayoutNode node, RectD rect, List<TileRect> sink)
    {
        if (node is LeafNode leaf)
        {
            sink.Add(new TileRect(leaf.TileId, rect));
            return;
        }

        foreach (var (child, childRect) in ChildRects((SplitNode)node, rect))
        {
            ProjectNode(child.Node, childRect, sink);
        }
    }

    private static LayoutNode? NormalizeNode(LayoutNode? node)
    {
        if (node is not SplitNode split)
        {
            return node;
        }

        var children = new List<SplitChild>();

        foreach (var child in split.Children)
        {
            var normalized = NormalizeNode(child.Node);
            if (normalized is null)
            {
                continue;
            }

            var ratio = double.IsFinite(child.Ratio) && child.Ratio > 0 ? child.Ratio : 0.0;

            if (normalized is SplitNode inner && inner.Orientation == split.Orientation)
            {
                children.AddRange(inner.Children.Select(g => new SplitChild(g.Node, ratio * g.Ratio)));
            }
            else
            {
                children.Add(new SplitChild(normalized, ratio));
            }
        }

        if (children.Count == 0)
        {
            return null;
        }

        if (children.Count == 1)
        {
            return children[0].Node;
        }

        var sum = children.Sum(c => c.Ratio);

        if (children.Any(c => c.Ratio <= LayoutConstants.Epsilon))
        {
            // Восстановление после повреждённых долей: равные доли всем детям.
            var equalShare = 1.0 / children.Count;
            for (var i = 0; i < children.Count; i++)
            {
                children[i] = children[i] with { Ratio = equalShare };
            }
        }
        else if (Math.Abs(sum - 1.0) > LayoutConstants.Epsilon)
        {
            // Деление выполняется только при реальном расхождении: иначе оно
            // сдвигало бы уже корректные доли на последний бит мантиссы.
            for (var i = 0; i < children.Count; i++)
            {
                children[i] = children[i] with { Ratio = children[i].Ratio / sum };
            }
        }

        // Список переписывается на месте: ссылки на узлы остаются валидными
        // (за них держатся хендлы сплиттеров во время drag).
        split.Children.Clear();
        split.Children.AddRange(children);
        return split;
    }

    private static bool ValidateNode(LayoutNode node, Orientation? parentOrientation)
    {
        if (node is not SplitNode split)
        {
            return true;
        }

        if (split.Children.Count < 2 || parentOrientation == split.Orientation)
        {
            return false;
        }

        if (Math.Abs(split.Children.Sum(c => c.Ratio) - 1.0) > 1e-6)
        {
            return false;
        }

        return split.Children.All(c => c.Ratio > 0 && ValidateNode(c.Node, split.Orientation));
    }

    private bool TryFind(Guid tileId, out LeafNode leaf, out SplitNode? parent, out int index)
    {
        leaf = null!;
        parent = null;
        index = -1;

        if (Root is LeafNode rootLeaf && rootLeaf.TileId == tileId)
        {
            leaf = rootLeaf;
            return true;
        }

        return Root is SplitNode split && TryFindIn(split, tileId, out leaf, out parent, out index);
    }

    private static bool TryFindIn(SplitNode node, Guid tileId, out LeafNode leaf, out SplitNode? parent, out int index)
    {
        for (var i = 0; i < node.Children.Count; i++)
        {
            switch (node.Children[i].Node)
            {
                case LeafNode candidate when candidate.TileId == tileId:
                    leaf = candidate;
                    parent = node;
                    index = i;
                    return true;

                case SplitNode nested when TryFindIn(nested, tileId, out leaf, out parent, out index):
                    return true;
            }
        }

        leaf = null!;
        parent = null;
        index = -1;
        return false;
    }

    private RectD? FindNodeRect(LayoutNode node)
    {
        foreach (var (candidate, rect) in EnumerateNodes(RectD.Unit))
        {
            if (ReferenceEquals(candidate, node))
            {
                return rect;
            }
        }

        return null;
    }
}
