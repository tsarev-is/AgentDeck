using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AgentDeck.ViewModels;

namespace AgentDeck.Views;

/// <summary>
/// Плейсхолдер нового тайла: поле рабочей директории и кнопки запуска CLI.
/// </summary>
public partial class TilePlaceholderView : UserControl
{
    /// <summary>
    /// Создаёт представление плейсхолдера.
    /// </summary>
    public TilePlaceholderView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnLaunchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LaunchOptionViewModel option } && DataContext is TileViewModel tile)
        {
            tile.RequestLaunch(option);
        }
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
        => (DataContext as TileViewModel)?.RequestSettings();
}
