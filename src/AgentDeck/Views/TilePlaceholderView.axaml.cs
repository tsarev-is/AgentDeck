using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AgentDeck.ViewModels;

namespace AgentDeck.Views;

/// <summary>
/// Плейсхолдер нового тайла: поле рабочей директории, выбор вложенной папки
/// с поиском и кнопки запуска CLI.
/// </summary>
public partial class TilePlaceholderView : UserControl
{
    private readonly TextBox? _filter;

    private bool _attached;

    /// <summary>
    /// Создаёт представление плейсхолдера.
    /// </summary>
    public TilePlaceholderView()
    {
        InitializeComponent();
        _filter = this.FindControl<TextBox>("FilterBox");
        DataContextChanged += (_, _) => BeginBrowse();

        // Стрелки поле поиска считает своими — перемещением каретки, — поэтому
        // подписки из разметки не хватает: слушаем и погашенные события.
        _filter?.AddHandler(KeyDownEvent, OnFilterKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        BeginBrowse();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnLaunchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LaunchOptionViewModel option } && DataContext is TileViewModel tile)
        {
            tile.RequestLaunch(option);
        }
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
        => (DataContext as TileViewModel)?.RequestSettings();

    private void OnUpClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as TileViewModel)?.GoUp();
        FocusFilter();
    }

    private void OnFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FolderEntryViewModel folder } && DataContext is TileViewModel tile)
        {
            tile.EnterFolder(folder);
            FocusFilter();
        }
    }

    /// <summary>
    /// Нажатие на «… N more» раскрывает следующую порцию папок. Фокус вернуть
    /// нужно: кнопку уничтожает та же пересборка полосы, которую она запускает.
    /// </summary>
    private void OnShowMoreClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as TileViewModel)?.ShowMoreFolders();
        FocusFilter();
    }

    /// <summary>
    /// Клавиатура в поле поиска: Enter входит в первую найденную папку, Esc
    /// сбрасывает фильтр, Alt+↑ поднимает на уровень выше.
    /// </summary>
    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TileViewModel tile)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = tile.EnterFirstFolder();
                break;

            case Key.Escape when tile.Filter.Length > 0:
                tile.Filter = string.Empty;
                e.Handled = true;
                break;

            // Выход из папки повешен на Alt+↑ — ту же пару, что у кнопки рядом
            // с путём. Backspace на эту роль не годится: поле стирает им символ
            // и гасит событие, а стёрло ли оно хоть что-то, к моменту
            // обработчика уже не видно — текст пуст в обоих случаях.
            case Key.Up when e.KeyModifiers == KeyModifiers.Alt:
                e.Handled = tile.GoUp();
                break;
        }
    }

    /// <summary>
    /// Возвращает ввод в поле поиска. Переход по папкам — не конечное действие,
    /// а шаг вглубь: следующий шаг снова начинается с набора имени, и искать
    /// поле мышью после каждого щелчка не должно быть нужно.
    /// </summary>
    private void FocusFilter() => _filter?.Focus();

    /// <summary>
    /// Просит тайл перечитать вложенные папки. Пока плейсхолдер не попал в
    /// дерево, файловую систему не трогаем: тайл могли и не показать.
    /// </summary>
    private void BeginBrowse()
    {
        if (_attached && DataContext is TileViewModel tile)
        {
            tile.BeginBrowse();
        }
    }
}
