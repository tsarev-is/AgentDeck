namespace AgentDeck.Layout;

/// <summary>
/// Визуальный сплиттер — граница между двумя соседними детьми одного сплита.
/// </summary>
/// <param name="Node">
/// Сплит, которому принадлежит граница.
/// </param>
/// <param name="Index">
/// Индекс ребёнка слева/сверху от границы.
/// </param>
/// <param name="Position">
/// Координата границы в пикселях вдоль оси сплита.
/// </param>
public readonly record struct SplitterHandle(SplitNode Node, int Index, double Position)
{
    /// <summary>
    /// Ось, вдоль которой двигается граница.
    /// </summary>
    public Orientation Orientation => Node.Orientation;
}

/// <summary>
/// Захваченные под курсором сплиттеры: по одному на ось. Обе оси сразу —
/// это угол между четырьмя тайлами, и тянуть надо оба.
/// </summary>
/// <param name="Columns">
/// Вертикальная граница: двигается по X.
/// </param>
/// <param name="Rows">
/// Горизонтальная граница: двигается по Y.
/// </param>
public readonly record struct SplitterGrab(SplitterHandle? Columns, SplitterHandle? Rows)
{
    /// <summary>
    /// Под курсором нет ни одной границы.
    /// </summary>
    public bool IsEmpty => Columns is null && Rows is null;

    /// <summary>
    /// Под курсором угол — обе оси.
    /// </summary>
    public bool IsCorner => Columns is not null && Rows is not null;
}

/// <summary>
/// Hit-testing сплиттеров — чистая функция точки, дерева и границ дека.
/// </summary>
public static class SplitterHitTester
{
    /// <summary>
    /// Ширина полосы захвата границы в пикселях.
    /// </summary>
    public const double GrabBand = 6;

    /// <summary>
    /// Находит границы под точкой: до одной на каждую ось, ближайшие к курсору.
    /// </summary>
    public static SplitterGrab HitTest(double x, double y, LayoutTree tree, RectD bounds, double grabBand = GrabBand)
    {
        SplitterHandle? columns = null;
        SplitterHandle? rows = null;
        var bestColumns = double.MaxValue;
        var bestRows = double.MaxValue;
        var half = grabBand / 2;

        foreach (var (node, rect) in tree.EnumerateNodes(bounds))
        {
            if (node is not SplitNode split)
            {
                continue;
            }

            var horizontal = split.Orientation == Orientation.Horizontal;

            // Точка обязана лежать в поперечном диапазоне сплита, иначе это
            // граница другого поддерева, случайно совпавшая по координате.
            var acrossOk = horizontal
                ? y >= rect.Y - half && y <= rect.Bottom + half
                : x >= rect.X - half && x <= rect.Right + half;

            if (!acrossOk)
            {
                continue;
            }

            var offset = horizontal ? rect.X : rect.Y;
            var extent = horizontal ? rect.W : rect.H;
            var probe = horizontal ? x : y;

            for (var i = 0; i < split.Children.Count - 1; i++)
            {
                offset += split.Children[i].Ratio * extent;
                var distance = Math.Abs(probe - offset);

                if (distance > half)
                {
                    continue;
                }

                if (horizontal && distance < bestColumns)
                {
                    bestColumns = distance;
                    columns = new SplitterHandle(split, i, offset);
                }
                else if (!horizontal && distance < bestRows)
                {
                    bestRows = distance;
                    rows = new SplitterHandle(split, i, offset);
                }
            }
        }

        return new SplitterGrab(columns, rows);
    }

    /// <summary>
    /// Возвращает текущую позицию границы: во время перетаскивания она меняется,
    /// а хендл держит только ссылку на узел и индекс.
    /// </summary>
    public static double PositionOf(SplitterHandle handle, LayoutTree tree, RectD bounds)
    {
        foreach (var (node, rect) in tree.EnumerateNodes(bounds))
        {
            if (!ReferenceEquals(node, handle.Node))
            {
                continue;
            }

            var horizontal = handle.Node.Orientation == Orientation.Horizontal;
            var offset = horizontal ? rect.X : rect.Y;
            var extent = horizontal ? rect.W : rect.H;

            for (var i = 0; i <= handle.Index && i < handle.Node.Children.Count; i++)
            {
                offset += handle.Node.Children[i].Ratio * extent;
            }

            return offset;
        }

        return handle.Position;
    }
}
