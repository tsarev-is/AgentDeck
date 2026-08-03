namespace AgentDeck.Layout;

/// <summary>
/// Тип цели броска тайла.
/// </summary>
public enum DropTargetKind
{
    /// <summary>
    /// Обмен местами с тайлом-целью.
    /// </summary>
    Swap,

    /// <summary>
    /// Сплит тайла-цели по указанной грани.
    /// </summary>
    EdgeSplit,

    /// <summary>
    /// Док к краю окна.
    /// </summary>
    Dock,
}

/// <summary>
/// Разрешённая цель броска.
/// </summary>
/// <param name="Kind">
/// Тип цели.
/// </param>
/// <param name="TileId">
/// Тайл-цель; для дока не задан.
/// </param>
/// <param name="Side">
/// Сторона вставки; для обмена не используется.
/// </param>
public readonly record struct DropTarget(DropTargetKind Kind, Guid TileId, DockSide Side)
{
    /// <summary>
    /// Обмен местами с указанным тайлом.
    /// </summary>
    public static DropTarget Swap(Guid tileId) => new(DropTargetKind.Swap, tileId, DockSide.Left);

    /// <summary>
    /// Сплит указанного тайла по грани.
    /// </summary>
    public static DropTarget EdgeSplit(Guid tileId, DockSide side) => new(DropTargetKind.EdgeSplit, tileId, side);

    /// <summary>
    /// Док к краю окна.
    /// </summary>
    public static DropTarget Dock(DockSide side) => new(DropTargetKind.Dock, Guid.Empty, side);
}

/// <summary>
/// Резолвер целей перетаскивания — чистая функция точки, дерева и границ дека.
/// Приоритеты: край окна, затем центр тайла, затем грани тайла.
/// </summary>
public static class DropZoneResolver
{
    /// <summary>
    /// Ширина полосы дока вдоль края окна в пикселях.
    /// </summary>
    public const double WindowEdgeBand = 24;

    /// <summary>
    /// Доля тайла по каждой оси, занятая центральной зоной обмена.
    /// </summary>
    public const double CenterFraction = 0.5;

    /// <summary>
    /// Определяет цель броска для точки. Возвращает null, если цели нет либо
    /// мутация нарушила бы минимальный размер тайла.
    /// </summary>
    public static DropTarget? Resolve(double x, double y, LayoutTree tree, RectD bounds, Guid draggedTileId)
    {
        if (tree.Root is null || bounds.W <= 0 || bounds.H <= 0)
        {
            return null;
        }

        // 1. Полосы вдоль краёв окна приоритетнее зон тайлов.
        if (ResolveWindowEdge(x, y, bounds) is { } dockSide)
        {
            return tree.CanInsertAtRootEdge(dockSide, draggedTileId) ? DropTarget.Dock(dockSide) : null;
        }

        var host = tree.Project(bounds).FirstOrDefault(t => t.Rect.Contains(x, y));
        if (host.Rect.W <= 0 || host.TileId == draggedTileId)
        {
            return null;
        }

        var dx = (x - host.Rect.X) / host.Rect.W;
        var dy = (y - host.Rect.Y) / host.Rect.H;
        var margin = (1 - CenterFraction) / 2;

        // 2. Центр тайла — обмен местами; геометрия не меняется, запрет невозможен.
        if (dx >= margin && dx <= 1 - margin && dy >= margin && dy <= 1 - margin)
        {
            return DropTarget.Swap(host.TileId);
        }

        // 3. Ближайшая грань тайла — сплит.
        var side = NearestSide(dx, dy);
        return tree.CanInsertAtEdge(host.TileId, side, draggedTileId)
            ? DropTarget.EdgeSplit(host.TileId, side)
            : null;
    }

    /// <summary>
    /// Возвращает точную результирующую геометрию перетаскиваемого тайла —
    /// то, что подсвечивается оверлеем во время drag.
    /// </summary>
    public static RectD? PreviewRect(DropTarget target, LayoutTree tree, RectD bounds, Guid draggedTileId)
    {
        if (target.Kind == DropTargetKind.Swap)
        {
            return tree.RectOf(target.TileId, bounds);
        }

        var probe = tree.Clone();
        return Apply(probe, target, draggedTileId) ? probe.RectOf(draggedTileId, bounds) : null;
    }

    /// <summary>
    /// Применяет цель броска к дереву. Перенос выполняется как вставка на новое
    /// место и последующее удаление старого листа — дыра невозможна.
    /// </summary>
    public static bool Apply(LayoutTree tree, DropTarget target, Guid draggedTileId)
    {
        if (!tree.Contains(draggedTileId))
        {
            return false;
        }

        if (target.Kind == DropTargetKind.Swap)
        {
            return tree.Swap(draggedTileId, target.TileId);
        }

        var placeholder = Guid.NewGuid();

        var inserted = target.Kind == DropTargetKind.EdgeSplit
            ? tree.InsertAtEdge(target.TileId, target.Side, placeholder)
            : tree.InsertAtRootEdge(target.Side, placeholder);

        if (!inserted)
        {
            return false;
        }

        return tree.Remove(draggedTileId) && tree.Rename(placeholder, draggedTileId);
    }

    /// <summary>
    /// Возвращает край окна, к полосе которого относится точка, либо null.
    /// </summary>
    private static DockSide? ResolveWindowEdge(double x, double y, RectD bounds)
    {
        var distances = new (DockSide Side, double Distance)[]
        {
            (DockSide.Left, x - bounds.X),
            (DockSide.Right, bounds.Right - x),
            (DockSide.Top, y - bounds.Y),
            (DockSide.Bottom, bounds.Bottom - y),
        };

        var nearest = distances.MinBy(d => d.Distance);
        return nearest.Distance <= WindowEdgeBand ? nearest.Side : null;
    }

    /// <summary>
    /// Возвращает ближайшую грань тайла для точки в его нормированных координатах.
    /// </summary>
    private static DockSide NearestSide(double dx, double dy)
    {
        var toLeft = dx;
        var toRight = 1 - dx;
        var toTop = dy;
        var toBottom = 1 - dy;

        var horizontal = Math.Min(toLeft, toRight);
        var vertical = Math.Min(toTop, toBottom);

        if (horizontal <= vertical)
        {
            return toLeft <= toRight ? DockSide.Left : DockSide.Right;
        }

        return toTop <= toBottom ? DockSide.Top : DockSide.Bottom;
    }
}
