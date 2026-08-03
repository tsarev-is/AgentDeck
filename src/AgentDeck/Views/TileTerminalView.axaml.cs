using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AgentDeck.Terminal;
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

    /// <summary>
    /// Буфер обмена окна; null, пока контрол не попал в дерево.
    /// </summary>
    private IClipboard? Clipboard => TopLevel.GetTopLevel(this)?.Clipboard;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _terminal = this.FindControl<TerminalControl>("Terminal");

        // Сочетания перехватываются на спуске события: собственный обработчик
        // контрола первым же делом снимает выделение и отправляет процессу
        // управляющий символ — на всплытии копировать было бы уже нечего.
        _terminal?.AddHandler(KeyDownEvent, OnTerminalKeyDown, RoutingStrategies.Tunnel);

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

    /// <summary>
    /// Клавиатура в части буфера обмена; остальные нажатия уходят процессу.
    /// </summary>
    private void OnTerminalKeyDown(object? sender, KeyEventArgs e)
    {
        if (_tile?.Terminal is not { } host)
        {
            return;
        }

        switch (ClipboardGesture.Resolve(e.Key, e.KeyModifiers, host.HasSelection))
        {
            case ClipboardAction.Copy:
                // Сочетание гасится и при пустом выделении: Ctrl+Shift+C, дошедший
                // до процесса, прервал бы агента вместо копирования.
                e.Handled = true;
                Copy(host);
                break;

            case ClipboardAction.Paste:
                e.Handled = true;
                Paste(host);
                break;
        }
    }

    /// <summary>
    /// Пока процесс сам следит за мышью, правая кнопка принадлежит ему: меню
    /// отменяем, иначе оно погасило бы событие и процесс получил бы нажатие
    /// без отпускания.
    /// </summary>
    private void OnMenuOpening(object? sender, CancelEventArgs e)
        => e.Cancel = _tile?.Terminal?.IsMouseReporting == true;

    private void OnCopyClick(object? sender, RoutedEventArgs e) => Invoke(Copy);

    private void OnPasteClick(object? sender, RoutedEventArgs e) => Invoke(Paste);

    private void OnSelectAllClick(object? sender, RoutedEventArgs e) => Invoke(host => host.SelectAll());

    /// <summary>
    /// Выполняет действие меню над терминалом тайла и возвращает ввод в него:
    /// на время показа меню фокус уходил в попап, и следующий набранный символ
    /// не попал бы в агента.
    /// </summary>
    private void Invoke(Action<TerminalHost> action)
    {
        if (_tile?.Terminal is { } host)
        {
            action(host);
        }

        _terminal?.Focus();
    }

    /// <summary>
    /// Кладёт выделение в буфер обмена и снимает его. Само снять его контрол не
    /// может — сочетание погашено и до него не дошло, — а живое выделение делает
    /// копированием и следующий Ctrl+C: прервать разогнавшегося агента стало бы
    /// нечем, пока пользователь не щёлкнет по терминалу.
    /// </summary>
    private void Copy(TerminalHost host)
    {
        if (Clipboard is not { } clipboard || host.SelectedText is not { Length: > 0 } text)
        {
            return;
        }

        host.ClearSelection();
        _ = CopyAsync(clipboard, text);
    }

    /// <summary>
    /// Вставляет текст из буфера обмена в ввод процесса.
    /// </summary>
    private void Paste(TerminalHost host)
    {
        if (Clipboard is { } clipboard)
        {
            _ = PasteAsync(clipboard, host);
        }
    }

    /// <summary>
    /// Обмен с буфером асинхронен, а обработчик клавиши — нет, поэтому задача
    /// остаётся без ожидания. Отказ буфера обмена (нет владельца выделения,
    /// сервер не ответил) для тайла не событие: молча ничего не происходит.
    /// </summary>
    private static async Task CopyAsync(IClipboard clipboard, string text)
    {
        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception)
        {
            // Платформенный буфер обмена своих ошибок не типизирует.
        }
    }

    /// <summary>
    /// Читает буфер обмена и отдаёт текст процессу. Продолжение возвращается в
    /// UI-поток, поэтому экранную модель терминала трогать можно.
    /// </summary>
    private static async Task PasteAsync(IClipboard clipboard, TerminalHost host)
    {
        try
        {
            host.Paste(await clipboard.TryGetTextAsync());
        }
        catch (Exception)
        {
            // Кроме буфера обмена, своих ошибок не типизирует и запись в PTY: она
            // может разойтись с гашением процесса. Задачу никто не ожидает, и
            // исключение отсюда осталось бы необработанным.
        }
    }
}
