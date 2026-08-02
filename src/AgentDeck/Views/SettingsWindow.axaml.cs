using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AgentDeck.Settings;
using AgentDeck.ViewModels;

namespace AgentDeck.Views;

/// <summary>
/// Окно настроек: директория по умолчанию и таблица утилит. Закрывается
/// сохранёнными настройками либо null, если пользователь нажал «Cancel».
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// Создаёт окно поверх настроек по умолчанию (нужно предпросмотру XAML).
    /// </summary>
    public SettingsWindow()
        : this(new SettingsViewModel(AppSettings.CreateDefault()))
    {
    }

    /// <summary>
    /// Создаёт окно поверх указанной модели настроек.
    /// </summary>
    public SettingsWindow(SettingsViewModel model)
    {
        Model = model;
        DataContext = model;
        InitializeComponent();
    }

    /// <summary>
    /// Модель окна.
    /// </summary>
    public SettingsViewModel Model { get; }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnAddClick(object? sender, RoutedEventArgs e) => Model.AddUtility();

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: UtilityRowViewModel row })
        {
            Model.RemoveUtility(row);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Close(Model.ToSettings());
}
