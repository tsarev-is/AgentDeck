using AgentDeck.Settings;

namespace AgentDeck.ViewModels;

/// <summary>
/// Строка таблицы утилит в окне настроек.
/// </summary>
public sealed class UtilityRowViewModel : ViewModelBase
{
    private string _name;
    private string _command;
    private bool _isEnabled;

    /// <summary>
    /// Создаёт строку по сохранённой настройке утилиты.
    /// </summary>
    public UtilityRowViewModel(UtilityState utility)
    {
        Id = string.IsNullOrWhiteSpace(utility.Id) ? Guid.NewGuid().ToString("N") : utility.Id;
        _name = utility.Name ?? string.Empty;
        _command = utility.Command ?? string.Empty;
        _isEnabled = utility.Enabled;
    }

    /// <summary>
    /// Стабильный идентификатор утилиты.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Имя утилиты — подпись кнопки запуска.
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? string.Empty);
    }

    /// <summary>
    /// Путь или команда запуска.
    /// </summary>
    public string Command
    {
        get => _command;
        set => SetField(ref _command, value ?? string.Empty);
    }

    /// <summary>
    /// Утилита показывается в новом терминале.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetField(ref _isEnabled, value))
            {
                OnPropertyChanged(nameof(RowOpacity));
            }
        }
    }

    /// <summary>
    /// Выключенная строка гасится — как в макете.
    /// </summary>
    public double RowOpacity => _isEnabled ? 1.0 : 0.45;

    /// <summary>
    /// Сворачивает строку обратно в настройку утилиты.
    /// </summary>
    public UtilityState ToState() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        Command = Command.Trim(),
        Enabled = IsEnabled,
    };

    /// <summary>
    /// Пустая строка: ни имени, ни команды — такую при сохранении выбрасываем.
    /// </summary>
    public bool IsBlank => Name.Trim().Length == 0 && Command.Trim().Length == 0;
}
