namespace AgentDeck.Layout;

/// <summary>
/// Прямоугольник в нормированных координатах раскладки (0..1 по обеим осям).
/// </summary>
public readonly record struct RectD(double X, double Y, double W, double H)
{
    /// <summary>
    /// Единичный прямоугольник — полная область дека.
    /// </summary>
    public static RectD Unit => new(0, 0, 1, 1);

    /// <summary>
    /// Координата правой грани.
    /// </summary>
    public double Right => X + W;

    /// <summary>
    /// Координата нижней грани.
    /// </summary>
    public double Bottom => Y + H;

    /// <summary>
    /// Площадь прямоугольника.
    /// </summary>
    public double Area => W * H;

    /// <summary>
    /// Проверяет, что точка лежит внутри прямоугольника (правая и нижняя грани исключены).
    /// </summary>
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>
    /// Проверяет пересечение с другим прямоугольником по ненулевой площади.
    /// </summary>
    public bool IntersectsArea(RectD other, double epsilon)
        => X < other.Right - epsilon
           && other.X < Right - epsilon
           && Y < other.Bottom - epsilon
           && other.Y < Bottom - epsilon;

    /// <summary>
    /// Переводит прямоугольник из нормированных координат в координаты указанной области.
    /// </summary>
    public RectD Scale(RectD bounds)
        => new(bounds.X + X * bounds.W, bounds.Y + Y * bounds.H, W * bounds.W, H * bounds.H);
}
