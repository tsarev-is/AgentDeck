using System.Text.Json;
using AgentDeck.Layout;

namespace AgentDeck.Session;

/// <summary>
/// Чтение и запись состояния сессии в JSON. Запись атомарна, а любая ошибка
/// чтения даёт null — приложение стартует с чистой раскладки.
/// </summary>
public sealed class SessionStore
{
    /// <summary>
    /// Имя папки приложения внутри каталога конфигурации.
    /// </summary>
    public const string FolderName = "AgentDeck";

    /// <summary>
    /// Имя файла сессии.
    /// </summary>
    public const string FileName = "session.json";

    /// <summary>
    /// Создаёт хранилище. База по умолчанию — каталог конфигурации пользователя
    /// (<c>~/.config</c> на Linux); переопределяется в тестах.
    /// </summary>
    public SessionStore(string? baseDirectory = null)
    {
        var root = baseDirectory ?? ResolveDefaultBase();
        Directory = Path.Combine(root, FolderName);
        FilePath = Path.Combine(Directory, FileName);
    }

    /// <summary>
    /// Каталог, в котором лежит файл сессии.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// Полный путь к файлу сессии.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Читает состояние сессии; возвращает null при отсутствии файла,
    /// повреждении данных или чужой версии формата.
    /// </summary>
    public SessionState? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<SessionState>(
                File.ReadAllText(FilePath),
                LayoutSerializer.JsonOptions);

            return state?.LayoutVersion == LayoutSerializer.CurrentVersion ? state : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Атомарно записывает состояние сессии: сначала временный файл, затем move
    /// поверх старого — оборванная запись не оставит битого session.json.
    /// </summary>
    public bool Save(SessionState state)
    {
        var temporary = FilePath + ".tmp";

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, LayoutSerializer.JsonOptions));
            File.Move(temporary, FilePath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temporary);
            return false;
        }
    }

    /// <summary>
    /// Удаляет файл сессии.
    /// </summary>
    public void Delete() => TryDelete(FilePath);

    private static string ResolveDefaultBase()
    {
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return string.IsNullOrEmpty(applicationData)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : applicationData;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Удаление — best-effort: следующая запись всё равно перезапишет файл.
        }
    }
}
