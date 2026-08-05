using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AgentDeck.Terminal;
using AgentDeck.ViewModels;
using SvcSystems.UI.Terminal;
using XTerm.Events;

namespace AgentDeck.Views;

/// <summary>
/// Тело запущенного тайла — встроенный терминал поверх экранной модели хоста.
/// </summary>
public partial class TileTerminalView : UserControl
{
    private TerminalControl? _terminal;
    private TileViewModel? _tile;
    private XTerm.Terminal? _watchedEngine;

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

    /// <summary>
    /// Забирает ввод в терминал, как только тайл сменил плейсхолдер на него.
    /// </summary>
    /// <param name="change">
    /// Изменившееся свойство контрола.
    /// </param>
    /// <remarks>
    /// Фокус стоял на кнопке запуска, а она исчезла вместе с плейсхолдером —
    /// после запуска он не принадлежит никому, и первое, что пользователь
    /// наберёт, не дойдёт до агента: сначала нужно щёлкнуть по терминалу.
    /// Забрать его сразу нельзя: контрол в этот момент ещё не разложен, и фокус
    /// молча не переходит, — поэтому ждём раскладки.
    /// </remarks>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            Dispatcher.UIThread.Post(FocusTerminal, DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Переводит ввод в терминал, если тайл всё ещё показывает его и ввод никому
    /// не принадлежит.
    /// </summary>
    /// <remarks>
    /// Запуск мог сорваться, и тайл вернулся бы к плейсхолдеру. А поднялся тайл
    /// мог и не по щелчку пользователя: у окна настроек своя жизнь, и пока
    /// сосед печатает в другом тайле или в поле ввода, отбирать у него фокус
    /// нельзя — набранное ушло бы чужому агенту.
    /// </remarks>
    private void FocusTerminal()
    {
        if (!IsVisible || TopLevel.GetTopLevel(this) is not Window { IsActive: true } window)
        {
            return;
        }

        if (window.FocusManager?.GetFocusedElement() is TerminalControl or TextBox)
        {
            return;
        }

        _terminal?.Focus();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _terminal = this.FindControl<TerminalControl>("Terminal");

        // Сочетания перехватываются на спуске события: собственный обработчик
        // контрола первым же делом снимает выделение и отправляет процессу
        // управляющий символ — на всплытии копировать было бы уже нечего.
        _terminal?.AddHandler(KeyDownEvent, OnTerminalKeyDown, RoutingStrategies.Tunnel);

        // Кнопки мыши перехватываются там же и по той же причине: агент,
        // следящий за мышью, получил бы нажатие первым.
        _terminal?.AddHandler(PointerPressedEvent, OnTerminalPointerPressed, RoutingStrategies.Tunnel);
        _terminal?.AddHandler(PointerReleasedEvent, OnTerminalPointerReleased, RoutingStrategies.Tunnel);

        // Колесо — тоже на спуске: контрол на альтернативном экране прокрутил бы
        // пустоту, и до перевода в стрелки дело не дошло бы.
        _terminal?.AddHandler(PointerWheelChangedEvent, OnTerminalPointerWheelChanged, RoutingStrategies.Tunnel);

        // Отслеживание мыши возвращается агенту на всплытии: к этому времени
        // контрол уже закрыл выделение. Потерю захвата слушаем как страховку —
        // прерванная протяжка (закрытый тайл, уход окна) отпускания не даёт, а
        // выключенное отслеживание осталось бы выключенным навсегда.
        _terminal?.AddHandler(PointerReleasedEvent, OnTerminalSelectionFinished, RoutingStrategies.Bubble, handledEventsToo: true);
        _terminal?.AddHandler(PointerCaptureLostEvent, OnTerminalSelectionFinished, RoutingStrategies.Bubble, handledEventsToo: true);

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
    /// Оставляет полосе прокрутки её место, пока процесс пишет в обычный буфер.
    /// </summary>
    /// <remarks>
    /// Контрол держит полосу прокрутки в колонке шириной «по содержимому» и
    /// показывает её только тогда, когда в буфере появилась история. Пока
    /// прокрутка пуста, колонка пустая, и экран шире на её ширину: как только
    /// первая строка уходит вверх, тайл теряет три колонки, а агент перерисовывает
    /// весь свой интерфейс по новой ширине — со стороны это выглядит как
    /// поехавшая разметка. Полоса объявляется видимой с приоритетом анимации,
    /// потому что сам контрол переставляет это свойство на каждое обновление
    /// модели, а локальное значение анимационное не перебивает.
    /// На альтернативном экране полосу приходится прятать обратно: там контрол
    /// сжимает её колонку в ноль, и видимая полоса легла бы поверх последних
    /// колонок текста.
    /// </remarks>
    private void ReserveScrollBar()
    {
        if (_terminal?.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault() is not { } scrollBar)
        {
            return;
        }

        var alternate = _terminal.Model?.Terminal?.IsAlternateBufferActive == true;

        scrollBar.SetValue(
            ScrollBar.VisibilityProperty,
            alternate ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Visible,
            BindingPriority.Animation);
    }

    /// <summary>
    /// Привязывает контрол к экранной модели терминала текущего тайла.
    /// </summary>
    private void BindModel()
    {
        if (_terminal is null)
        {
            return;
        }

        if (_watchedEngine is not null)
        {
            _watchedEngine.BufferChanged -= OnBufferChanged;
        }

        _terminal.Model = _tile?.Terminal?.Model;
        _watchedEngine = _terminal.Model?.Terminal?.Engine;

        if (_watchedEngine is not null)
        {
            // Переключение экрана меняет и место полосы прокрутки.
            _watchedEngine.BufferChanged += OnBufferChanged;
        }

        ReserveScrollBar();
    }

    private void OnBufferChanged(object? sender, TerminalEvents.BufferChangedEventArgs e) => ReserveScrollBar();

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
    /// Мышь до контрола доходит не вся. Нажатие правой кнопки гасится совсем:
    /// полноэкранные TUI (агенты вроде claude, htop, vim) сами следят за мышью,
    /// и контрол отправил бы им нажатие — агент отработал бы клик под открытым
    /// меню. Фокус контролу возвращаем сами: снятое нажатие его туда не
    /// переводит.
    /// </summary>
    private void OnTerminalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(_terminal).Properties;

        if (properties.IsRightButtonPressed)
        {
            e.Handled = true;
            _terminal?.Focus();
            return;
        }

        // Левая кнопка в тайле принадлежит выделению, а не агенту, даже когда тот
        // следит за мышью: своё выделение агент рисует у себя на экране, и
        // скопировать его нечем — в буфер обмена попадает только выделение
        // терминала. Нажатие отдаём контролу дальше, выделение он ведёт сам, но
        // берётся за него лишь пока не видит отслеживания мыши, поэтому режим
        // гасим прямо здесь.
        if (properties.IsLeftButtonPressed)
        {
            _tile?.Terminal?.SuspendMouseTracking();
        }
    }

    /// <summary>
    /// Колесо в тайле полноэкранного приложения, которое не следит за мышью,
    /// уходит ему стрелками: прокрутки у альтернативного экрана нет, и контрол
    /// на такой поворот не отвечает ничем. Остальные повороты — его: он либо
    /// прокручивает буфер тайла, либо отдаёт их процессу событиями мыши.
    /// </summary>
    private void OnTerminalPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_tile?.Terminal?.ScrollAlternateScreen(e.Delta.Y) == true)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Протяжка закончилась (или её прервали) — отслеживание мыши возвращается
    /// процессу.
    /// </summary>
    /// <remarks>
    /// Отпускание правой кнопки протяжку не заканчивает: нажатая посреди
    /// выделения, она вернула бы агенту мышь прямо в жесте — контрол потерял бы
    /// выделение, а движения мыши ушли бы агенту в ввод.
    /// </remarks>
    private void OnTerminalSelectionFinished(object? sender, RoutedEventArgs e)
    {
        if (e is PointerReleasedEventArgs { InitialPressMouseButton: not MouseButton.Left })
        {
            return;
        }

        _tile?.Terminal?.ResumeMouseTracking();
    }

    /// <summary>
    /// Меню открывается по отпусканию правой кнопки — как это сделала бы сама
    /// Avalonia, если бы нажатие до неё дошло. Выделение для меню не нужно:
    /// без него вставка и «выделить всё» остаются доступны, а копирование
    /// гаснет само по пустому <c>HasSelection</c>.
    /// </summary>
    private void OnTerminalPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton is not MouseButton.Right)
            return;

        e.Handled = true;
        _terminal?.ContextMenu?.Open(_terminal);
    }

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
