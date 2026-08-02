using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using AgentDeck.Layout;
using AgentDeck.ViewModels;
using AgentDeck.Views;

namespace AgentDeck.Controls;

/// <summary>
/// Канва дека: раскладывает тайлы строго по проекции дерева раскладки и ведёт
/// pointer-логику тайлинга — перенос за заголовок и ресайз за рёбра и углы.
/// Своей геометрии не хранит — вся она берётся из <see cref="LayoutTree.Project(RectD)"/>.
/// </summary>
public class DeckCanvas : Panel
{
    /// <summary>
    /// Модель дека, тайлы которой отображает канва.
    /// </summary>
    public static readonly StyledProperty<DeckViewModel?> DeckProperty =
        AvaloniaProperty.Register<DeckCanvas, DeckViewModel?>(nameof(Deck));

    /// <summary>
    /// Порог в пикселях, после которого нажатие на заголовок становится переносом.
    /// </summary>
    private const double DragThreshold = 4;

    /// <summary>
    /// Затемнение исходного тайла на время переноса.
    /// </summary>
    private const double DraggedTileOpacity = 0.4;

    private static readonly Cursor SizeWestEast = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor SizeNorthSouth = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor SizeCorner = new(StandardCursorType.BottomRightCorner);
    private static readonly Cursor DragCursor = new(StandardCursorType.SizeAll);

    private readonly Dictionary<Guid, TileView> _views = [];
    private readonly DropOverlay _overlay = new();

    private DragMode _mode = DragMode.None;
    private IPointer? _capturedPointer;

    private Guid _dragTileId;
    private Point _pressPoint;
    private bool _dragStarted;
    private DropTarget? _dropTarget;

    private SplitterGrab _grab;
    private Point _lastPoint;
    private double _desiredX;
    private double _desiredY;

    private TopLevel? _keyboardHost;

    /// <summary>
    /// Создаёт канву и добавляет слой подсветки перетаскивания.
    /// </summary>
    public DeckCanvas()
    {
        Children.Add(_overlay);
        ClipToBounds = true;
    }

    private enum DragMode
    {
        None,
        Tile,
        Splitter,
    }

    /// <summary>
    /// Модель дека, тайлы которой отображает канва.
    /// </summary>
    public DeckViewModel? Deck
    {
        get => GetValue(DeckProperty);
        set => SetValue(DeckProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != DeckProperty)
        {
            return;
        }

        if (change.GetOldValue<DeckViewModel?>() is { } previous)
        {
            previous.LayoutChanged -= OnLayoutChanged;
            ((INotifyCollectionChanged)previous.Tiles).CollectionChanged -= OnTilesChanged;
        }

        if (change.GetNewValue<DeckViewModel?>() is { } current)
        {
            current.LayoutChanged += OnLayoutChanged;
            ((INotifyCollectionChanged)current.Tiles).CollectionChanged += OnTilesChanged;
        }

        SyncTileViews();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Esc отменяет перенос независимо от того, где сейчас фокус.
        _keyboardHost = TopLevel.GetTopLevel(this);
        if (_keyboardHost is not null)
        {
            _keyboardHost.AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        // Шрифт чипа наследуется от окна — тот же Barlow, что и в интерфейсе.
        _overlay.ChipTypeface = new Typeface(Avalonia.Controls.Documents.TextElement.GetFontFamily(this));
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_keyboardHost is not null)
        {
            _keyboardHost.RemoveHandler(KeyDownEvent, OnKeyDown);
            _keyboardHost = null;
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Канва занимает всё доступное место; при бесконечном ограничении отдаёт ноль,
        // чтобы не раздувать родительский контейнер.
        var size = new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

        _overlay.Measure(size);

        var measured = new HashSet<Guid>();

        if (Deck is not null)
        {
            // Тайл меряется своим прямоугольником из проекции, а не всей канвой:
            // иначе содержимое верстается по размеру дека и обрезается при Arrange.
            foreach (var tile in Deck.Layout.Project(new RectD(0, 0, size.Width, size.Height)))
            {
                if (_views.TryGetValue(tile.TileId, out var view))
                {
                    view.Measure(Clamp(tile.Rect, size).Size);
                    measured.Add(tile.TileId);
                }
            }
        }

        foreach (var (id, view) in _views)
        {
            if (!measured.Contains(id))
            {
                view.Measure(default);
            }
        }

        return size;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _overlay.Arrange(new Rect(finalSize));

        if (Deck is null)
        {
            return finalSize;
        }

        foreach (var tile in Deck.Layout.Project(new RectD(0, 0, finalSize.Width, finalSize.Height)))
        {
            if (_views.TryGetValue(tile.TileId, out var view))
            {
                view.Arrange(Clamp(tile.Rect, finalSize));
            }
        }

        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Deck is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(this);

        // Рёбра приоритетнее заголовка: полоса захвата лежит поверх его края.
        var grab = SplitterHitTester.HitTest(point.X, point.Y, Deck.Layout, PixelBounds);
        if (!grab.IsEmpty)
        {
            BeginSplitterDrag(grab, point, e);
            return;
        }

        if (FindHeaderTile(e.Source as Visual, point) is { } tileId)
        {
            _mode = DragMode.Tile;
            _dragTileId = tileId;
            _pressPoint = point;
            _dragStarted = false;
            _dropTarget = null;
            Capture(e.Pointer);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (Deck is null)
        {
            return;
        }

        var point = e.GetPosition(this);

        switch (_mode)
        {
            case DragMode.Splitter:
                DragSplitter(point);
                break;

            case DragMode.Tile:
                DragTile(point);
                break;

            default:
                UpdateHoverCursor(point);
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_mode == DragMode.None)
        {
            return;
        }

        if (_mode == DragMode.Tile && _dragStarted && _dropTarget is { } target && Deck is not null)
        {
            if (DropZoneResolver.Apply(Deck.Layout, target, _dragTileId))
            {
                Deck.NotifyLayoutChanged();
            }
        }

        EndDrag();
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        // Захват уже потерян — отпускать нечего, только чистим ссылку.
        _capturedPointer = null;
        EndDrag();
    }

    /// <summary>
    /// Прямоугольник канвы в пикселях — область проекции дерева.
    /// </summary>
    private RectD PixelBounds => new(0, 0, Bounds.Width, Bounds.Height);

    /// <summary>
    /// Ограничивает прямоугольник границами канвы, не трогая доли раскладки:
    /// при слишком малом окне тайлы просто прижимаются друг к другу.
    /// </summary>
    private static Rect Clamp(RectD rect, Size finalSize)
    {
        var x = Math.Clamp(rect.X, 0, Math.Max(0, finalSize.Width));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, finalSize.Height));
        var width = Math.Clamp(rect.W, 0, Math.Max(0, finalSize.Width - x));
        var height = Math.Clamp(rect.H, 0, Math.Max(0, finalSize.Height - y));

        return new Rect(x, y, width, height);
    }

    private void BeginSplitterDrag(SplitterGrab grab, Point point, PointerPressedEventArgs e)
    {
        _mode = DragMode.Splitter;
        _grab = grab;
        _lastPoint = point;
        _desiredX = grab.Columns?.Position ?? point.X;
        _desiredY = grab.Rows?.Position ?? point.Y;

        Cursor = CursorFor(grab);
        Capture(e.Pointer);
        e.Handled = true;
    }

    private void DragSplitter(Point point)
    {
        if (Deck is null)
        {
            return;
        }

        // Желаемая позиция накапливает полное движение мыши, а фактическая
        // берётся из дерева: после упора в min-size граница возвращается сразу.
        _desiredX += point.X - _lastPoint.X;
        _desiredY += point.Y - _lastPoint.Y;
        _lastPoint = point;

        var bounds = PixelBounds;
        var changed = false;

        if (_grab.Columns is { } columns && bounds.W > 0)
        {
            var current = SplitterHitTester.PositionOf(columns, Deck.Layout, bounds);
            changed |= Deck.Layout.ResizeSplitter(columns.Node, columns.Index, (_desiredX - current) / bounds.W);
        }

        if (_grab.Rows is { } rows && bounds.H > 0)
        {
            var current = SplitterHitTester.PositionOf(rows, Deck.Layout, bounds);
            changed |= Deck.Layout.ResizeSplitter(rows.Node, rows.Index, (_desiredY - current) / bounds.H);
        }

        if (changed)
        {
            InvalidateArrange();
            Deck.NotifyLayoutChanged();
        }
    }

    private void DragTile(Point point)
    {
        if (Deck is null)
        {
            return;
        }

        if (!_dragStarted)
        {
            var moved = Math.Abs(point.X - _pressPoint.X) + Math.Abs(point.Y - _pressPoint.Y);
            if (moved < DragThreshold)
            {
                return;
            }

            _dragStarted = true;
            Cursor = DragCursor;

            if (_views.TryGetValue(_dragTileId, out var source))
            {
                source.Opacity = DraggedTileOpacity;
            }
        }

        var bounds = PixelBounds;
        _dropTarget = DropZoneResolver.Resolve(point.X, point.Y, Deck.Layout, bounds, _dragTileId);

        if (_dropTarget is { } target)
        {
            // Раскладка во время drag не перестраивается — показываем только превью.
            var preview = DropZoneResolver.PreviewRect(target, Deck.Layout, bounds, _dragTileId);
            _overlay.Show(preview, target.Kind == DropTargetKind.Swap, point, ChipText());
        }
        else
        {
            _overlay.Show(null, false, point, ChipText());
        }
    }

    private string ChipText()
        => (Deck?.FindTile(_dragTileId)?.Title ?? string.Empty).ToUpper(System.Globalization.CultureInfo.CurrentCulture);

    private void UpdateHoverCursor(Point point)
    {
        if (Deck is null)
        {
            return;
        }

        var grab = SplitterHitTester.HitTest(point.X, point.Y, Deck.Layout, PixelBounds);
        Cursor = grab.IsEmpty ? Cursor.Default : CursorFor(grab);
    }

    private static Cursor CursorFor(SplitterGrab grab)
    {
        if (grab.IsCorner)
        {
            return SizeCorner;
        }

        return grab.Columns is not null ? SizeWestEast : SizeNorthSouth;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _mode == DragMode.None)
        {
            return;
        }

        // Отмена: цель сбрасывается до применения.
        _dropTarget = null;
        EndDrag();
        e.Handled = true;
    }

    /// <summary>
    /// Захватывает указатель на время переноса, запоминая его: отмена по Esc
    /// приходит не через pointer-событие, а отпустить захват всё равно нужно.
    /// </summary>
    private void Capture(IPointer pointer)
    {
        _capturedPointer = pointer;
        pointer.Capture(this);
    }

    private void EndDrag()
    {
        if (_mode == DragMode.None)
        {
            return;
        }

        // Иначе после Esc указатель остался бы прибит к канве, и другие
        // контролы перестали бы реагировать на мышь.
        _capturedPointer?.Capture(null);
        _capturedPointer = null;

        if (_views.TryGetValue(_dragTileId, out var source))
        {
            source.Opacity = 1;
        }

        _mode = DragMode.None;
        _dragStarted = false;
        _dropTarget = null;
        _grab = default;
        Cursor = Cursor.Default;
        _overlay.Hide();
    }

    /// <summary>
    /// Возвращает тайл, за заголовок которого нажали, либо null.
    /// </summary>
    private Guid? FindHeaderTile(Visual? source, Point point)
    {
        var view = source?.FindAncestorOfType<TileView>(includeSelf: true);

        if (view?.DataContext is not TileViewModel tile)
        {
            return null;
        }

        var local = this.TranslatePoint(point, view);
        return local is { } inTile && view.IsPointInHeader(inTile) ? tile.Id : null;
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        SyncTileViews();
        InvalidateMeasure();
        InvalidateArrange();
    }

    private void OnTilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncTileViews();

    /// <summary>
    /// Приводит набор дочерних контролов в соответствие с тайлами дека.
    /// </summary>
    private void SyncTileViews()
    {
        var tiles = Deck?.Tiles ?? (IReadOnlyList<TileViewModel>)[];
        var alive = tiles.Select(t => t.Id).ToHashSet();

        foreach (var id in _views.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            Children.Remove(_views[id]);
            _views.Remove(id);
        }

        foreach (var tile in tiles)
        {
            if (_views.ContainsKey(tile.Id))
            {
                continue;
            }

            var view = new TileView { DataContext = tile };
            _views[tile.Id] = view;

            // Оверлей всегда остаётся последним ребёнком — поверх тайлов.
            Children.Insert(Math.Max(0, Children.Count - 1), view);
        }
    }
}
