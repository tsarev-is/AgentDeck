using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AgentDeck.ViewModels;

namespace AgentDeck.Views;

/// <summary>
/// Тайл дека: рамка, заголовок со статусом и телом — плейсхолдером или терминалом.
/// </summary>
public partial class TileView : UserControl
{
    private Control? _header;

    /// <summary>
    /// Создаёт представление тайла.
    /// </summary>
    public TileView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _header = this.FindControl<Control>("Header");
    }

    /// <summary>
    /// Проверяет, попадает ли точка (в координатах тайла) в полосу заголовка —
    /// именно за неё тайл перетаскивается.
    /// </summary>
    public bool IsPointInHeader(Point point)
        => _header is not null && point.Y >= 0 && point.Y <= _header.Bounds.Height && point.X >= 0 && point.X <= Bounds.Width;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => (DataContext as TileViewModel)?.RequestClose();

    private void OnRestartClick(object? sender, RoutedEventArgs e) => (DataContext as TileViewModel)?.RequestRestart();
}
