namespace AgentDeck.Layout;

/// <summary>
/// Числовые константы раскладки.
/// </summary>
public static class LayoutConstants
{
    /// <summary>
    /// Минимальная ширина тайла в нормированных координатах.
    /// </summary>
    public const double MinWidth = 0.14;

    /// <summary>
    /// Минимальная высота тайла в нормированных координатах.
    /// </summary>
    public const double MinHeight = 0.12;

    /// <summary>
    /// Максимальное число одновременных тайлов.
    /// </summary>
    public const int MaxTiles = 8;

    /// <summary>
    /// Допуск для сравнения долей и координат.
    /// </summary>
    public const double Epsilon = 1e-9;

    /// <summary>
    /// Допуск, в пределах которого сумма долей сплита считается единицей и нормализуется.
    /// </summary>
    public const double RatioTolerance = 0.01;

    /// <summary>
    /// Возвращает минимальный размер тайла вдоль указанной оси.
    /// </summary>
    public static double MinExtent(Orientation axis)
        => axis == Orientation.Horizontal ? MinWidth : MinHeight;
}
