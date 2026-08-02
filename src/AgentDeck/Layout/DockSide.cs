namespace AgentDeck.Layout;

/// <summary>
/// Сторона вставки — грань тайла или край окна.
/// </summary>
public enum DockSide
{
    /// <summary>
    /// Левая грань.
    /// </summary>
    Left,

    /// <summary>
    /// Правая грань.
    /// </summary>
    Right,

    /// <summary>
    /// Верхняя грань.
    /// </summary>
    Top,

    /// <summary>
    /// Нижняя грань.
    /// </summary>
    Bottom,
}

/// <summary>
/// Помощники для сторон вставки.
/// </summary>
public static class DockSideExtensions
{
    /// <summary>
    /// Возвращает ориентацию сплита, вдоль которого лежит указанная сторона.
    /// </summary>
    public static Orientation Axis(this DockSide side)
        => side is DockSide.Left or DockSide.Right ? Orientation.Horizontal : Orientation.Vertical;

    /// <summary>
    /// Возвращает true, если новый элемент встаёт перед целевым в порядке детей сплита.
    /// </summary>
    public static bool IsLeading(this DockSide side) => side is DockSide.Left or DockSide.Top;
}
