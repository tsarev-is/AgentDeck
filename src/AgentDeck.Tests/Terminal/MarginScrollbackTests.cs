using System.Text;
using AgentDeck.Terminal;
using NUnit.Framework;
using SvcSystems.UI.Terminal;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Прокрутка при регионе, прижатом к верху экрана: буфер терминала пополняет
/// прокрутку только при регионе во весь экран, и без правки история такого тайла
/// пропадает — так рисует свои сообщения codex.
/// </summary>
[TestFixture]
public class MarginScrollbackTests
{
    /// <summary>
    /// Высота экрана в тестах.
    /// </summary>
    private const int Rows = 24;

    /// <summary>
    /// Ширина экрана в тестах.
    /// </summary>
    private const int Cols = 40;

    /// <summary>
    /// Строка, ушедшая вверх из региона, прижатого к верху экрана, попадает в
    /// прокрутку — как в настоящем терминале.
    /// </summary>
    [Test]
    public void Feed_LineFeedAtAnchoredBottomMargin_MovesLineToScrollback()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[1;4rtop\u001b[4;1Hlast\n");

        Assert.Multiple(() =>
        {
            Assert.That(model.Terminal.Buffer.YBase, Is.EqualTo(1), "прокрутка выросла на строку");
            Assert.That(model.CanScroll, Is.True, "прокручивать стало что");
            Assert.That(RowText(model, 0), Is.EqualTo("top"), "ушедшая строка лежит в прокрутке");
        });
    }

    /// <summary>
    /// Прокрутка региона поднимает только его строки: то, что нарисовано ниже
    /// нижней границы, остаётся на своих местах.
    /// </summary>
    [Test]
    public void Feed_LineFeedAtAnchoredBottomMargin_KeepsRowsBelowRegion()
    {
        var (model, margins) = Screen();

        // Строки 1..4 — регион, строки 5 и 24 — интерфейс под ним.
        Feed(margins, "\u001b[5;1Hui\u001b[24;1Hstatus");
        Feed(margins, "\u001b[1;4r\u001b[1;1Hone\u001b[4;1Htwo\n");

        var visible = model.Terminal.Buffer.YBase;

        Assert.Multiple(() =>
        {
            Assert.That(RowText(model, visible + 2), Is.EqualTo("two"), "нижняя строка региона поднялась");
            Assert.That(RowText(model, visible + 3), Is.EqualTo(string.Empty), "на границе региона стало пусто");
            Assert.That(RowText(model, visible + 4), Is.EqualTo("ui"), "строка под регионом не двинулась");
            Assert.That(RowText(model, visible + 23), Is.EqualTo("status"), "последняя строка экрана не двинулась");
        });
    }

    /// <summary>
    /// Курсор после перевода строки на границе региона стоит там же, где его
    /// оставил бы разбор: строка не меняется, столбец сбрасывается.
    /// </summary>
    [Test]
    public void Feed_LineFeedAtAnchoredBottomMargin_LeavesCursorOnMargin()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[1;4r\u001b[4;1Hone\nnext");

        var buffer = model.Terminal.Buffer;

        Assert.Multiple(() =>
        {
            Assert.That(buffer.Y, Is.EqualTo(3), "курсор остался на нижней границе региона");
            Assert.That(buffer.X, Is.EqualTo(4), "следующий текст лёг с начала строки");
            Assert.That(RowText(model, buffer.YBase + 3), Is.EqualTo("next"), "и попал на границу региона");
        });
    }

    /// <summary>
    /// Перевод строки приходит и в виде <c>ESC D</c> (IND): строка так же уходит
    /// в прокрутку, но столбец курсора эта форма сохраняет.
    /// </summary>
    [Test]
    public void Feed_IndexAtAnchoredBottomMargin_MovesLineToScrollbackKeepingColumn()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[1;4rtop\u001b[4;1Hlast\u001bD");

        var buffer = model.Terminal.Buffer;

        Assert.Multiple(() =>
        {
            Assert.That(buffer.YBase, Is.EqualTo(1), "прокрутка выросла на строку");
            Assert.That(RowText(model, 0), Is.EqualTo("top"), "ушедшая строка лежит в прокрутке");
            Assert.That(buffer.Y, Is.EqualTo(3), "курсор остался на нижней границе региона");
            Assert.That(buffer.X, Is.EqualTo(4), "а столбец не сбросился");
        });
    }

    /// <summary>
    /// <c>ESC E</c> (NEL) — перевод строки с возвратом каретки: строка уходит в
    /// прокрутку, курсор встаёт в первый столбец.
    /// </summary>
    [Test]
    public void Feed_NextLineAtAnchoredBottomMargin_MovesLineToScrollbackAndReturnsCarriage()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[1;4rtop\u001b[4;1Hlast\u001bE");

        var buffer = model.Terminal.Buffer;

        Assert.Multiple(() =>
        {
            Assert.That(buffer.YBase, Is.EqualTo(1), "прокрутка выросла на строку");
            Assert.That(RowText(model, 0), Is.EqualTo("top"), "ушедшая строка лежит в прокрутке");
            Assert.That(buffer.Y, Is.EqualTo(3), "курсор остался на нижней границе региона");
            Assert.That(buffer.X, Is.Zero, "а столбец сбросился");
        });
    }

    /// <summary>
    /// Поход процесса в пейджер (альтернативный экран и обратно) прокрутку тайла
    /// не выключает: границы региона остаются на месте, и следующая ушедшая
    /// строка обязана попасть в прокрутку. Так codex открывает <c>git log</c>.
    /// </summary>
    [Test]
    public void Feed_AfterAlternateScreenRoundTrip_KeepsFillingScrollback()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[1;4rbefore\u001b[4;1Hlast\n");
        Feed(margins, "\u001b[?1049h\u001b[1;1Hpager\n\u001b[?1049l");
        Feed(margins, "\u001b[1;1Hafter\u001b[4;1Hlast\n");

        Assert.Multiple(() =>
        {
            Assert.That(model.Terminal.IsAlternateBufferActive, Is.False, "пейджер закрылся");
            Assert.That(model.Terminal.Buffer.YBase, Is.EqualTo(2), "прокрутка выросла на обе строки");
            Assert.That(RowText(model, 0), Is.EqualTo("before"), "строка до пейджера сохранилась");
            Assert.That(RowText(model, 1), Is.EqualTo("after"), "и строка после него тоже");
        });
    }

    /// <summary>
    /// <c>ESC[nS</c> прокручивает регион независимо от курсора — этой
    /// последовательностью codex сдвигает историю, когда его интерфейс дошёл до
    /// низа экрана.
    /// </summary>
    [Test]
    public void Feed_ScrollUpInAnchoredRegion_MovesLinesToScrollback()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[1;1Hone\u001b[2;1Htwo\u001b[3;1Hthree");
        Feed(margins, "\u001b[1;4r\u001b[2S\u001b[r");

        Assert.Multiple(() =>
        {
            Assert.That(model.Terminal.Buffer.YBase, Is.EqualTo(2), "прокрутка выросла на обе строки");
            Assert.That(RowText(model, 0), Is.EqualTo("one"), "первая ушедшая строка сохранилась");
            Assert.That(RowText(model, 1), Is.EqualTo("two"), "и вторая тоже");
            Assert.That(RowText(model, model.Terminal.Buffer.YBase), Is.EqualTo("three"), "экран прокрутился");
        });
    }

    /// <summary>
    /// Прокрутка больше высоты региона уносит вверх только его строки: из
    /// четырёхстрочного региона может уйти четыре строки, а не весь экран.
    /// Иначе прокрутка набивалась бы пустотой, и вид с полосой прыгали бы на всю
    /// высоту экрана.
    /// </summary>
    [Test]
    public void Feed_ScrollUpBeyondRegionHeight_AddsOnlyRegionLines()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[1;1Hone\u001b[2;1Htwo\u001b[3;1Hthree\u001b[4;1Hfour");
        Feed(margins, "\u001b[1;4r\u001b[999S\u001b[r");

        Assert.Multiple(() =>
        {
            Assert.That(model.Terminal.Buffer.YBase, Is.EqualTo(4), "прокрутка выросла на высоту региона");
            Assert.That(RowText(model, 0), Is.EqualTo("one"), "строки региона сохранились");
            Assert.That(RowText(model, 3), Is.EqualTo("four"), "все четыре");
            Assert.That(RowText(model, 4), Is.EqualTo(string.Empty), "а экран после прокрутки пуст");
        });
    }

    /// <summary>
    /// Регион, не прижатый к верху экрана, трогать нельзя: настоящий терминал
    /// такие строки тоже выбрасывает, и буфер делает это сам.
    /// </summary>
    [Test]
    public void Feed_RegionBelowTopOfScreen_LeavesScrollbackEmpty()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[2;4r\u001b[4;1Hlast\n");

        Assert.Multiple(() =>
        {
            Assert.That(model.Terminal.Buffer.YBase, Is.Zero, "прокрутка не пополняется");
            Assert.That(RowText(model, 2), Is.EqualTo("last"), "строка поднялась внутри региона");
        });
    }

    /// <summary>
    /// Регион во весь экран буфер обрабатывает верно и без правки — вмешательство
    /// не должно ничего менять.
    /// </summary>
    [Test]
    public void Feed_FullScreenRegion_FillsScrollbackAsUsual()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[24;1Hlast\n\n");

        Assert.Multiple(() =>
        {
            Assert.That(model.Terminal.Buffer.YBase, Is.EqualTo(2), "прокрутка выросла на оба перевода строки");
            Assert.That(RowText(model, 23), Is.EqualTo("last"), "строка ушла в прокрутку");
        });
    }

    /// <summary>
    /// У альтернативного экрана прокрутки нет: там прокрутка региона обязана
    /// остаться прежней, иначе экран поедет.
    /// </summary>
    [Test]
    public void Feed_AlternateScreen_LeavesScrollbackEmpty()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[?1049h\u001b[1;4r\u001b[4;1Hlast\n");

        Assert.Multiple(() =>
        {
            Assert.That(model.Terminal.IsAlternateBufferActive, Is.True, "альтернативный экран включён");
            Assert.That(model.Terminal.Buffer.YBase, Is.Zero, "прокрутки у него нет");
            Assert.That(RowText(model, 2), Is.EqualTo("last"), "строка поднялась внутри региона");
        });
    }

    /// <summary>
    /// Перевод строки внутри строковой последовательности (заголовок окна) не
    /// исполняется — разбор не должен принять его за прокрутку.
    /// </summary>
    [Test]
    public void Feed_LineFeedInsideOscString_DoesNotScroll()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[1;4r\u001b[4;1Hlast\u001b]0;a\nb");

        Assert.Multiple(() =>
        {
            Assert.That(model.Terminal.Buffer.YBase, Is.Zero, "прокрутка не пополнялась");
            Assert.That(RowText(model, 3), Is.EqualTo("last"), "строка на границе региона осталась на месте");
        });
    }

    /// <summary>
    /// Порции вывода приходят произвольной длины, и разрез не должен менять
    /// результат — даже если он попал внутрь последовательности или между ESC и
    /// её вторым байтом.
    /// </summary>
    [Test]
    public void Feed_StreamSplitAtEveryOffset_MatchesWholeStream()
    {
        const string stream = "\u001b[1;4r\u001b[1;1Hone\u001b[4;1Htwo\nthree\u001bDfour\n\u001b[1S"
            + "\u001b[?1049h\u001b[1;1Hpager\u001b[?1049l\u001b[4;1Hlast\n\u001b[r\u001b[5;1Hui";

        var (whole, wholeMargins) = Screen();
        Feed(wholeMargins, stream);

        var expected = Snapshot(whole);

        for (var split = 1; split < stream.Length; split++)
        {
            var (model, margins) = Screen();

            Feed(margins, stream[..split]);
            Feed(margins, stream[split..]);

            Assert.That(Snapshot(model), Is.EqualTo(expected), $"разрез на {split} байте меняет экран");
        }
    }

    /// <summary>
    /// Разбор перед перезапуском процесса начинается заново: незакрытая
    /// последовательность прошлого процесса не должна влиять на новый.
    /// </summary>
    [Test]
    public void Reset_AfterUnfinishedSequence_StartsParsingAnew()
    {
        var (model, margins) = Screen();

        Feed(margins, "\u001b[1;4r\u001b[4;1Hlast\u001b[");
        margins.Reset();
        model.Terminal.Engine.Reset();

        Feed(margins, "\u001b[1;4r\u001b[4;1Hafter\n");

        Assert.That(model.Terminal.Buffer.YBase, Is.EqualTo(1), "прокрутка снова пополняется");
    }

    /// <summary>
    /// Создаёт экранную модель тайла и правщик прокрутки к ней.
    /// </summary>
    private static (TerminalControlModel Model, MarginScrollback Margins) Screen()
    {
        var model = new TerminalControlModel(new TerminalOptions
        {
            Cols = Cols,
            Rows = Rows,
            Scrollback = 200,
            ConvertEol = true,
        });

        return (model, new MarginScrollback(model));
    }

    /// <summary>
    /// Отдаёт текст правщику как порцию вывода процесса.
    /// </summary>
    /// <param name="margins">
    /// Правщик прокрутки.
    /// </param>
    /// <param name="text">
    /// Порция вывода.
    /// </param>
    private static void Feed(MarginScrollback margins, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        margins.Feed(bytes, bytes.Length);
    }

    /// <summary>
    /// Возвращает текст строки буфера без хвостовых пробелов.
    /// </summary>
    /// <param name="model">
    /// Экранная модель терминала.
    /// </param>
    /// <param name="index">
    /// Номер строки в буфере, включая прокрутку.
    /// </param>
    private static string RowText(TerminalControlModel model, int index)
    {
        if (model.Terminal.Buffer.GetLine(index) is not { } line)
        {
            return string.Empty;
        }

        var text = new StringBuilder();

        for (var column = 0; column < line.Length; column++)
        {
            text.Append(line[column].Content);
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Снимает весь буфер вместе с прокруткой и положением курсора.
    /// </summary>
    /// <param name="model">
    /// Экранная модель терминала.
    /// </param>
    private static string Snapshot(TerminalControlModel model)
    {
        var buffer = model.Terminal.Buffer;
        var text = new StringBuilder();

        text.Append($"ybase={buffer.YBase} x={buffer.X} y={buffer.Y} region={buffer.ScrollTop}..{buffer.ScrollBottom}\n");

        for (var index = 0; index < buffer.Lines.Length; index++)
        {
            text.Append(RowText(model, index)).Append('\n');
        }

        return text.ToString();
    }
}
