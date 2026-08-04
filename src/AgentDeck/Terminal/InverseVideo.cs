using SvcSystems.UI.Terminal;
using XTerm.Buffer;

namespace AgentDeck.Terminal;

/// <summary>
/// Возвращает на экран инверсию цветов (SGR 7) — ею агенты рисуют каретку ввода.
/// </summary>
/// <remarks>
/// Контрол терминала показывает инверсию, меняя местами коды цветов ячейки. Но
/// «по умолчанию» у него не цвет, а роль: коды 256 и 257 он разворачивает в
/// белый, когда красит текст, и в чёрный, когда красит фон. У ячейки, где оба
/// цвета по умолчанию, обмен кодами ничего не меняет — инверсия пропадает.
/// Именно так рисует каретку Claude Code: аппаратный курсор он гасит, а ячейку
/// под ним отдаёт инверсной, и без правки от каретки на экране не остаётся
/// следа — не видно, где стоит ввод и что удалит Backspace.
/// Замена цветов по умолчанию на те же цвета, заданные явно, вид обычных ячеек
/// не меняет, а инверсным возвращает смысл обмена.
/// </remarks>
public sealed class InverseVideo
{
    /// <summary>
    /// Код цвета текста по умолчанию в атрибутах ячейки.
    /// </summary>
    private const int DefaultForeground = 256;

    /// <summary>
    /// Код цвета фона по умолчанию в атрибутах ячейки.
    /// </summary>
    private const int DefaultBackground = 257;

    /// <summary>
    /// Палитровый белый — в него контрол разворачивает цвет текста по умолчанию.
    /// </summary>
    private const int PaletteWhite = 15;

    /// <summary>
    /// Палитровый чёрный — в него контрол разворачивает цвет фона по умолчанию.
    /// </summary>
    private const int PaletteBlack = 0;

    private readonly TerminalControlModel _model;

    private TerminalBuffer? _watched;
    private int _trimmed;
    private int _scrollBase;

    /// <summary>
    /// Создаёт правщик инверсии для экранной модели.
    /// </summary>
    /// <param name="model">
    /// Экранная модель терминала.
    /// </param>
    /// <remarks>
    /// Создавать нужно до первого вывода: правщик считает ушедшие вниз строки с
    /// момента создания, и пропущенное начало он уже не восстановит.
    /// </remarks>
    public InverseVideo(TerminalControlModel model)
    {
        _model = model;
        WatchTrimming(model.Terminal.Buffer);
    }

    /// <summary>
    /// Задаёт явные цвета инверсным ячейкам, написанным с прошлого прохода.
    /// </summary>
    /// <returns>
    /// true, если хоть одна ячейка изменилась и экран нужно перестроить.
    /// </returns>
    public bool Normalize()
    {
        var terminal = _model.Terminal;
        var buffer = terminal.Buffer;

        WatchTrimming(buffer);

        // Активный экран отсчитывается от YBase, а не от видимой области: пока
        // пользователь смотрит прокрутку, процесс пишет всё равно вниз буфера.
        var bottom = Math.Min(buffer.YBase + terminal.Rows, buffer.Lines.Length);
        var top = Math.Max(bottom - terminal.Rows - TakeScrolledLines(buffer), 0);
        var changed = false;

        for (var index = top; index < bottom; index++)
        {
            if (buffer.GetLine(index) is { } line)
            {
                changed |= NormalizeLine(line);
            }
        }

        return changed;
    }

    /// <summary>
    /// Возвращает число строк, ушедших вниз буфера с прошлого прохода, и
    /// начинает счёт заново.
    /// </summary>
    /// <param name="buffer">
    /// Активный буфер терминала.
    /// </param>
    /// <returns>
    /// Сколько строк добавилось снизу; 0, если счёт сбился.
    /// </returns>
    /// <remarks>
    /// Порция вывода может прокрутить экран целиком, и без этой добавки
    /// инверсия уехала бы в прокрутку неправленой: следующий проход туда уже не
    /// заглянет.
    /// </remarks>
    private int TakeScrolledLines(TerminalBuffer buffer)
    {
        // Пока прокрутка не заполнена, новая строка поднимает YBase; дальше
        // буфер вытесняет старые строки, и рост виден только по вытеснениям.
        var scrolled = buffer.YBase - _scrollBase + _trimmed;

        _scrollBase = buffer.YBase;
        _trimmed = 0;

        // Ресайз и переключение на альтернативный экран двигают YBase сами по
        // себе, поэтому разность бывает и отрицательной.
        return Math.Max(scrolled, 0);
    }

    /// <summary>
    /// Переводит подписку на вытеснение строк на текущий буфер: у
    /// альтернативного экрана он свой.
    /// </summary>
    /// <param name="buffer">
    /// Активный буфер терминала.
    /// </param>
    private void WatchTrimming(TerminalBuffer buffer)
    {
        if (ReferenceEquals(_watched, buffer))
        {
            return;
        }

        if (_watched is not null)
        {
            _watched.Trimmed -= OnTrimmed;
        }

        // Отписки на гашении не нужно: буфер и правщик принадлежат одной
        // экранной модели и уходят вместе с тайлом.
        _watched = buffer;
        buffer.Trimmed += OnTrimmed;
    }

    private void OnTrimmed(int lines) => _trimmed += lines;

    /// <summary>
    /// Правит инверсные ячейки одной строки буфера.
    /// </summary>
    /// <param name="line">
    /// Строка буфера.
    /// </param>
    /// <returns>
    /// true, если хоть одна ячейка изменилась.
    /// </returns>
    private static bool NormalizeLine(BufferLine line)
    {
        var changed = false;

        for (var column = 0; column < line.Length; column++)
        {
            var cell = line[column];
            var attributes = cell.Attributes;

            if (!attributes.IsInverse())
            {
                continue;
            }

            var foreground = attributes.GetFgColor();
            var background = attributes.GetBgColor();

            // Ячейке с обоими своими цветами обмен и так удаётся.
            if (!IsRoleDefault(foreground) && !IsRoleDefault(background))
            {
                continue;
            }

            if (IsRoleDefault(foreground))
            {
                attributes.SetFgColor(PaletteWhite);
            }

            if (IsRoleDefault(background))
            {
                attributes.SetBgColor(PaletteBlack);
            }

            cell.Attributes = attributes;
            line[column] = cell;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Показывает, что контрол развернёт код не в его собственный цвет, а в цвет
    /// по роли ячейки.
    /// </summary>
    /// <param name="color">
    /// Код цвета из атрибутов ячейки.
    /// </param>
    /// <returns>
    /// true, если код разворачивается по роли.
    /// </returns>
    /// <remarks>
    /// Контрол сравнивает код с обоими значениями по умолчанию, не глядя на
    /// режим цвета и на роль, в которой код стоит. Поэтому по роли он
    /// разворачивает и 24-битный цвет, случайно равный такому коду
    /// (<c>#000100</c>, <c>#000101</c>): подменять их на палитровые — значит
    /// сохранить вид ячейки, а оставить как есть — потерять инверсию.
    /// </remarks>
    private static bool IsRoleDefault(int color) => color is DefaultForeground or DefaultBackground;
}
