using AgentDeck.Models;
using AgentDeck.Settings;

namespace AgentDeck.ViewModels;

/// <summary>
/// Кнопка запуска одной утилиты на плейсхолдере тайла.
/// </summary>
public sealed class LaunchOptionViewModel : ViewModelBase
{
    private bool _isSuggested;

    /// <summary>
    /// Создаёт вариант запуска по настройке утилиты.
    /// </summary>
    public LaunchOptionViewModel(UtilityState utility)
        : this(utility.Name, utility.Command)
    {
    }

    /// <summary>
    /// Создаёт вариант запуска с явными именем и командой.
    /// </summary>
    public LaunchOptionViewModel(string? name, string? command)
    {
        Name = (name ?? string.Empty).Trim();
        Command = (command ?? string.Empty).Trim();
        Kind = AgentKindProfile.Resolve(Name, Command);
    }

    /// <summary>
    /// Имя утилиты — подпись кнопки.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Команда, уходящая в shell. Пустая — интерактивный shell.
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Профиль паттернов статуса, выведенный из имени и команды.
    /// </summary>
    public AgentKind Kind { get; }

    /// <summary>
    /// Кнопка утилиты, сохранённой в прошлой сессии: акцентируется для запуска в один клик.
    /// </summary>
    public bool IsSuggested
    {
        get => _isSuggested;
        set => SetField(ref _isSuggested, value);
    }
}
