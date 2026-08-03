namespace AgentDeck.ViewModels;

/// <summary>
/// Кнопка-чип вложенной директории на плейсхолдере тайла.
/// </summary>
public sealed class FolderEntryViewModel
{
    /// <summary>
    /// Создаёт чип по имени директории и её полному пути.
    /// </summary>
    /// <param name="name">
    /// Имя директории — подпись чипа.
    /// </param>
    /// <param name="path">
    /// Полный путь — подсказка чипа.
    /// </param>
    public FolderEntryViewModel(string name, string path)
    {
        Name = name;
        FullPath = path;
    }

    /// <summary>
    /// Имя директории.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Путь, в который перейдёт тайл по нажатию.
    /// </summary>
    public string FullPath { get; }
}
