using System.Text.Json;
using AgentDeck.Layout;

namespace AgentDeck.Settings;

/// <summary>
/// Чтение и запись настроек в JSON. Запись атомарна, а любая ошибка чтения даёт
/// настройки по умолчанию — приложение всегда стартует с рабочим набором утилит.
/// </summary>
public sealed class SettingsStore
{
    /// <summary>
    /// Имя папки приложения внутри каталога конфигурации.
    /// </summary>
    public const string FolderName = "AgentDeck";

    /// <summary>
    /// Имя файла настроек.
    /// </summary>
    public const string FileName = "settings.json";

    /// <summary>
    /// Создаёт хранилище. База по умолчанию — каталог конфигурации пользователя
    /// (<c>~/.config</c> на Linux); переопределяется в тестах.
    /// </summary>
    public SettingsStore(string? baseDirectory = null)
    {
        var root = baseDirectory ?? ResolveDefaultBase();
        Directory = Path.Combine(root, FolderName);
        FilePath = Path.Combine(Directory, FileName);
    }

    /// <summary>
    /// Каталог, в котором лежит файл настроек.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// Полный путь к файлу настроек.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Читает настройки; при отсутствии файла, повреждении данных или чужой
    /// версии формата возвращает настройки по умолчанию.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return AppSettings.CreateDefault();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(FilePath),
                LayoutSerializer.JsonOptions);

            return settings?.SettingsVersion == AppSettings.CurrentVersion
                ? settings
                : AppSettings.CreateDefault();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return AppSettings.CreateDefault();
        }
    }

    /// <summary>
    /// Атомарно записывает настройки: сначала временный файл, затем move поверх
    /// старого — оборванная запись не оставит битого settings.json.
    /// </summary>
    public bool Save(AppSettings settings)
    {
        var temporary = FilePath + ".tmp";

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, LayoutSerializer.JsonOptions));
            File.Move(temporary, FilePath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temporary);
            return false;
        }
    }

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
