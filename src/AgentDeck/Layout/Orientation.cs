namespace AgentDeck.Layout;

/// <summary>
/// Ориентация сплита: вдоль какой оси доли делят область узла.
/// </summary>
public enum Orientation
{
    /// <summary>
    /// Дети выстроены слева направо, доли делят ширину.
    /// </summary>
    Horizontal,

    /// <summary>
    /// Дети выстроены сверху вниз, доли делят высоту.
    /// </summary>
    Vertical,
}
