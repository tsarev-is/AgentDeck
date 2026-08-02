using AgentDeck.Models;

namespace AgentDeck.Settings;

/// <summary>
/// Настройка одной утилиты (CLI-агента): подпись кнопки запуска и команда,
/// которая уходит в shell.
/// </summary>
public sealed class UtilityState
{
    /// <summary>
    /// Стабильный идентификатор строки; переживает переименование утилиты.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Имя утилиты — подпись кнопки запуска на плейсхолдере.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Путь или команда запуска. Пустая строка — обычный интерактивный shell.
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// Выключенные утилиты не показываются в новом терминале.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Настройки приложения: директория по умолчанию и список утилит.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Текущая версия формата настроек; чужая версия отбрасывает весь файл.
    /// </summary>
    public const int CurrentVersion = 1;

    private List<UtilityState> _utilities = [];

    /// <summary>
    /// Версия формата настроек.
    /// </summary>
    public int SettingsVersion { get; set; } = CurrentVersion;

    /// <summary>
    /// Директория, подставляемая в новый плейсхолдер. Пустая строка — домашняя папка.
    /// </summary>
    public string? DefaultDirectory { get; set; }

    /// <summary>
    /// Утилиты в порядке отображения. Присвоение null трактуется как пустой
    /// список — «utilities»: null в файле не должен ронять загрузку.
    /// </summary>
    public List<UtilityState> Utilities
    {
        get => _utilities;
        set => _utilities = value ?? [];
    }

    /// <summary>
    /// Настройки чистой установки: домашняя директория и штатный набор CLI.
    /// «script» приходит с пустой командой — это обычный shell.
    /// </summary>
    public static AppSettings CreateDefault() => new()
    {
        SettingsVersion = CurrentVersion,
        DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Utilities =
        [
            .. AgentKindNames.All.Select(kind => new UtilityState
            {
                Id = kind.ToString(),
                Name = kind.CommandName(),
                Command = kind == AgentKind.Script ? string.Empty : kind.CommandName(),
                Enabled = true,
            }),
        ],
    };

    /// <summary>
    /// Копия настроек — рабочий экземпляр для окна настроек, чтобы «Отмена»
    /// не оставляла следов в применённых настройках.
    /// </summary>
    public AppSettings Clone() => new()
    {
        SettingsVersion = SettingsVersion,
        DefaultDirectory = DefaultDirectory,
        Utilities =
        [
            .. Utilities.Select(utility => new UtilityState
            {
                Id = utility.Id,
                Name = utility.Name,
                Command = utility.Command,
                Enabled = utility.Enabled,
            }),
        ],
    };

    /// <summary>
    /// Утилиты, показываемые на плейсхолдере: включённые и с непустым именем.
    /// </summary>
    public IReadOnlyList<UtilityState> EnabledUtilities()
        => [.. Utilities.Where(utility => utility.Enabled && !string.IsNullOrWhiteSpace(utility.Name))];
}
