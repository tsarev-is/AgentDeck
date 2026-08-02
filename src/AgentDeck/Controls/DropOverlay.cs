using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AgentDeck.Layout;

namespace AgentDeck.Controls;

/// <summary>
/// Слой подсветки перетаскивания: показывает точную результирующую геометрию
/// и ghost-чип заголовка у курсора. Событий не перехватывает.
/// </summary>
public sealed class DropOverlay : Control
{
    private static readonly Color Accent = Color.FromRgb(0x59, 0x80, 0xa6);

    private readonly IBrush _fill = new SolidColorBrush(Accent, 0.22);
    private readonly IPen _edge = new Pen(new SolidColorBrush(Accent), 2);
    private readonly IBrush _chipBackground = new SolidColorBrush(Color.FromRgb(0x1d, 0x1f, 0x20), 0.92);
    private readonly IPen _chipEdge = new Pen(new SolidColorBrush(Accent), 1);
    private readonly IBrush _chipForeground = new SolidColorBrush(Color.FromRgb(0x94, 0xbc, 0xe3));

    private RectD? _preview;
    private bool _isSwap;
    private Point? _chipPosition;
    private string? _chipText;
    private Typeface _chipTypeface = Typeface.Default;

    /// <summary>
    /// Создаёт оверлей, прозрачный для ввода.
    /// </summary>
    public DropOverlay()
    {
        IsHitTestVisible = false;
        IsVisible = false;
    }

    /// <summary>
    /// Шрифт ghost-чипа.
    /// </summary>
    public Typeface ChipTypeface
    {
        get => _chipTypeface;
        set => _chipTypeface = value;
    }

    /// <summary>
    /// Показывает подсветку цели и чип у курсора.
    /// </summary>
    public void Show(RectD? preview, bool isSwap, Point chipPosition, string chipText)
    {
        _preview = preview;
        _isSwap = isSwap;
        _chipPosition = chipPosition;
        _chipText = chipText;
        IsVisible = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Скрывает подсветку.
    /// </summary>
    public void Hide()
    {
        _preview = null;
        _chipPosition = null;
        _chipText = null;
        IsVisible = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (_preview is { } preview)
        {
            var rect = new Rect(preview.X, preview.Y, preview.W, preview.H);

            // Обмен подсвечивает все четыре грани цели, вставка — контур будущего места.
            context.FillRectangle(_fill, rect);
            context.DrawRectangle(null, _edge, rect.Deflate(1));

            if (_isSwap)
            {
                context.DrawRectangle(null, _edge, rect.Deflate(4));
            }
        }

        if (_chipPosition is { } position && !string.IsNullOrEmpty(_chipText))
        {
            DrawChip(context, position, _chipText);
        }
    }

    private void DrawChip(DrawingContext context, Point position, string text)
    {
        var layout = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _chipTypeface,
            12,
            _chipForeground);

        var chip = new Rect(
            position.X + 12,
            position.Y + 12,
            layout.Width + 16,
            layout.Height + 8);

        context.FillRectangle(_chipBackground, chip);
        context.DrawRectangle(null, _chipEdge, chip);
        context.DrawText(layout, new Point(chip.X + 8, chip.Y + 4));
    }
}
