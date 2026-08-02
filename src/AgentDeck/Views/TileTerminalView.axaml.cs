using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AgentDeck.ViewModels;
using SvcSystems.UI.Terminal;

namespace AgentDeck.Views;

/// <summary>
/// Тело запущенного тайла — встроенный терминал поверх экранной модели хоста.
/// </summary>
public partial class TileTerminalView : UserControl
{
    private TerminalControl? _terminal;
    private TileViewModel? _tile;

    /// <summary>
    /// Создаёт представление терминала.
    /// </summary>
    public TileTerminalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _terminal = this.FindControl<TerminalControl>("Terminal");
        OnDataContextChanged(this, EventArgs.Empty);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_tile is not null)
        {
            _tile.PropertyChanged -= OnTilePropertyChanged;
        }

        _tile = DataContext as TileViewModel;

        if (_tile is not null)
        {
            _tile.PropertyChanged += OnTilePropertyChanged;
        }

        BindModel();
    }

    private void OnTilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Хост терминала появляется только при первом запуске CLI.
        if (e.PropertyName is nameof(TileViewModel.Terminal))
        {
            BindModel();
        }
    }

    /// <summary>
    /// Привязывает контрол к экранной модели терминала текущего тайла.
    /// </summary>
    private void BindModel()
    {
        if (_terminal is not null)
        {
            _terminal.Model = _tile?.Terminal?.Model;
        }
    }
}
