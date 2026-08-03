namespace AgentDeck.ViewModels;

/// <summary>
/// Чип «показать ещё» в полосе вложенных папок: за ним остаётся то, что нашлось,
/// но не влезло в показанную порцию.
/// </summary>
public sealed class MoreFoldersViewModel
{
    /// <summary>
    /// Создаёт чип по числу скрытых папок.
    /// </summary>
    /// <param name="count">
    /// Сколько найденных папок осталось за границей порции.
    /// </param>
    public MoreFoldersViewModel(int count) => Count = count;

    /// <summary>
    /// Число скрытых папок.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Подпись чипа.
    /// </summary>
    public string Label => $"… {Count} more";
}
