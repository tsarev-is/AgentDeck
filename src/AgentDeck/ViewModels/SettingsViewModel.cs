using System.Collections.ObjectModel;
using AgentDeck.Settings;

namespace AgentDeck.ViewModels;

/// <summary>
/// Модель окна настроек: директория по умолчанию и таблица утилит. Работает на
/// копии настроек — «Отмена» не оставляет следов.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    /// <summary>
    /// Имя, подставляемое новой строке таблицы.
    /// </summary>
    public const string NewUtilityName = "utility";

    private readonly ObservableCollection<UtilityRowViewModel> _utilities = [];

    private string _defaultDirectory;

    /// <summary>
    /// Создаёт модель поверх текущих настроек.
    /// </summary>
    public SettingsViewModel(AppSettings settings)
    {
        var source = settings.Clone();

        _defaultDirectory = source.DefaultDirectory ?? string.Empty;
        Utilities = new ReadOnlyObservableCollection<UtilityRowViewModel>(_utilities);

        foreach (var utility in source.Utilities)
        {
            Add(new UtilityRowViewModel(utility));
        }
    }

    /// <summary>
    /// Директория, подставляемая в новый плейсхолдер.
    /// </summary>
    public string DefaultDirectory
    {
        get => _defaultDirectory;
        set => SetField(ref _defaultDirectory, value ?? string.Empty);
    }

    /// <summary>
    /// Строки таблицы утилит.
    /// </summary>
    public ReadOnlyObservableCollection<UtilityRowViewModel> Utilities { get; }

    /// <summary>
    /// Добавляет пустую включённую строку.
    /// </summary>
    public UtilityRowViewModel AddUtility()
    {
        var row = new UtilityRowViewModel(new UtilityState
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"{NewUtilityName} {_utilities.Count + 1}",
            Command = string.Empty,
            Enabled = true,
        });

        Add(row);
        return row;
    }

    /// <summary>
    /// Удаляет строку из таблицы.
    /// </summary>
    public void RemoveUtility(UtilityRowViewModel row)
    {
        row.RemoveRequested -= OnRemoveRequested;
        _utilities.Remove(row);
    }

    /// <summary>
    /// Собирает настройки для сохранения: пустые строки отбрасываются,
    /// имена и команды обрезаются по краям.
    /// </summary>
    public AppSettings ToSettings() => new()
    {
        SettingsVersion = AppSettings.CurrentVersion,
        DefaultDirectory = DefaultDirectory.Trim(),
        Utilities = [.. _utilities.Where(row => !row.IsBlank).Select(row => row.ToState())],
    };

    private void Add(UtilityRowViewModel row)
    {
        row.RemoveRequested += OnRemoveRequested;
        _utilities.Add(row);
    }

    private void OnRemoveRequested(object? sender, EventArgs e)
    {
        if (sender is UtilityRowViewModel row)
        {
            RemoveUtility(row);
        }
    }
}
