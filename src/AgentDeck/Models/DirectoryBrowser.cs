namespace AgentDeck.Models;

/// <summary>
/// Результат чтения каталога.
/// </summary>
/// <param name="Folders">
/// Имена вложенных директорий без скрытых, отсортированные по алфавиту.
/// </param>
/// <param name="Exists">
/// Каталог существует и доступен для чтения.
/// </param>
public sealed record DirectoryListing(IReadOnlyList<string> Folders, bool Exists)
{
    /// <summary>
    /// Пустой результат для несуществующего или недоступного каталога.
    /// </summary>
    public static readonly DirectoryListing Missing = new([], false);
}

/// <summary>
/// Чтение вложенных директорий для выбора рабочей папки в новом тайле.
/// </summary>
public class DirectoryBrowser
{
    /// <summary>
    /// Читает имена вложенных директорий, кроме скрытых. Ошибки файловой
    /// системы наружу не выходят: недоступный каталог выглядит так же, как
    /// отсутствующий, — плейсхолдеру достаточно знать, что выбирать не из чего.
    /// </summary>
    /// <param name="expandedPath">
    /// Путь с уже раскрытым «~».
    /// </param>
    public virtual DirectoryListing List(string? expandedPath)
    {
        if (string.IsNullOrWhiteSpace(expandedPath) || !Directory.Exists(expandedPath))
        {
            return DirectoryListing.Missing;
        }

        try
        {
            var folders = new DirectoryInfo(expandedPath)
                .EnumerateDirectories("*", ListingOptions())
                .Select(static directory => directory.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new DirectoryListing(folders, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Каталог исчез или закрыт правами прямо во время чтения.
            return DirectoryListing.Missing;
        }
    }

    /// <summary>
    /// Скрытые и системные папки в выдачу не попадают: «.git», «.cache» и
    /// подобные только забивают список, из которого выбирают рабочий каталог.
    /// Перейти в такую папку по-прежнему можно — путь вводится в поле руками.
    /// </summary>
    /// <remarks>
    /// На Unix скрытыми считаются имена с точки в начале — атрибут
    /// <see cref="FileAttributes.Hidden"/> платформа выставляет им сама.
    /// </remarks>
    private static EnumerationOptions ListingOptions() => new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };
}
