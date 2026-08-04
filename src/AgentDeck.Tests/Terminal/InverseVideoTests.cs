using AgentDeck.Terminal;
using NUnit.Framework;
using SvcSystems.UI.Terminal;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Правка инверсии цветов в буфере: контрол показывает её обменом кодов, и
/// обмену нужны явные цвета вместо значений по умолчанию.
/// </summary>
[TestFixture]
public class InverseVideoTests
{
    /// <summary>
    /// Код цвета текста по умолчанию.
    /// </summary>
    private const int DefaultForeground = 256;

    /// <summary>
    /// Код цвета фона по умолчанию.
    /// </summary>
    private const int DefaultBackground = 257;

    /// <summary>
    /// Высота экрана в тестах.
    /// </summary>
    private const int Rows = 24;

    /// <summary>
    /// Инверсная ячейка с цветами по умолчанию получает те же цвета, заданные
    /// явно: только тогда обмен местами даёт на экране инверсию. Так рисует
    /// каретку Claude Code, и без правки её не видно вовсе.
    /// </summary>
    [Test]
    public void Normalize_InverseWithDefaultColors_MakesColorsExplicit()
    {
        var (model, video) = Screen();

        model.Feed("\u001b[7mr\u001b[27m");

        Assert.That(video.Normalize(), Is.True, "ячейка должна измениться");

        var cell = CellAt(model, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(cell.Foreground, Is.EqualTo(15), "цвет текста — палитровый белый");
            Assert.That(cell.Background, Is.EqualTo(0), "цвет фона — палитровый чёрный");
            Assert.That(cell.Inverse, Is.True, "признак инверсии остаётся на месте");
        });
    }

    /// <summary>
    /// Правка не трогает символ ячейки: инверсия меняет цвета, а не текст —
    /// иначе выделение и детектор статусов забрали бы из буфера мусор.
    /// </summary>
    [Test]
    public void Normalize_InverseCell_KeepsText()
    {
        var (model, video) = Screen();

        model.Feed("hello wo\u001b[7mr\u001b[27mld");
        video.Normalize();

        Assert.That(model.Terminal.Engine.GetVisibleLines()[0].TrimEnd(), Is.EqualTo("hello world"));
    }

    /// <summary>
    /// Из пары цветов правка достаёт только тот, что задан по умолчанию: обмен
    /// с явным цветом контрол разворачивает правильно и сам.
    /// </summary>
    [Test]
    public void Normalize_InverseWithOneDefaultColor_KeepsExplicitColor()
    {
        var (model, video) = Screen();

        model.Feed("\u001b[31;7mr\u001b[27m");

        Assert.That(video.Normalize(), Is.True, "фон по умолчанию должен измениться");

        var cell = CellAt(model, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(cell.Foreground, Is.EqualTo(1), "заданный красный остаётся на месте");
            Assert.That(cell.Background, Is.EqualTo(0), "цвет фона становится явным");
        });
    }

    /// <summary>
    /// Инверсной ячейке с двумя явными цветами правка не нужна: обмен и так
    /// меняет вид, а лишняя запись сбросила бы кэш строки буфера.
    /// </summary>
    [Test]
    public void Normalize_InverseWithExplicitColors_ChangesNothing()
    {
        var (model, video) = Screen();

        model.Feed("\u001b[31;44;7mr\u001b[27m");

        Assert.That(video.Normalize(), Is.False);

        var cell = CellAt(model, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(cell.Foreground, Is.EqualTo(1));
            Assert.That(cell.Background, Is.EqualTo(4));
        });
    }

    /// <summary>
    /// 24-битный цвет, случайно равный коду цвета по умолчанию, тоже становится
    /// палитровым: контрол сравнивает код, не глядя на режим цвета, и такую
    /// ячейку он и до правки красил цветом по умолчанию. Оставить код как есть
    /// значило бы отдать обмену два цвета, которые контрол развернёт в один и
    /// тот же чёрный, — инверсия стала бы чёрным по чёрному.
    /// </summary>
    [Test]
    public void Normalize_RgbColorEqualToDefaultCode_BecomesPaletteColor()
    {
        var (model, video) = Screen();

        // 38;2;0;1;0 — RGB #000100, численно равный коду цвета текста по умолчанию.
        model.Feed("\u001b[38;2;0;1;0;7mr\u001b[27m");

        Assert.That(video.Normalize(), Is.True);

        var cell = CellAt(model, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(cell.Foreground, Is.EqualTo(15), "цвет текста — палитровый белый");
            Assert.That(cell.Background, Is.EqualTo(0), "цвет фона — палитровый чёрный");
        });
    }

    /// <summary>
    /// Обычные ячейки правка обходит: цвета по умолчанию у них так и остаются
    /// значениями по умолчанию, и вид экрана от прохода не меняется.
    /// </summary>
    [Test]
    public void Normalize_PlainCells_ChangeNothing()
    {
        var (model, video) = Screen();

        model.Feed("hello world");

        Assert.That(video.Normalize(), Is.False);

        var cell = CellAt(model, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(cell.Foreground, Is.EqualTo(DefaultForeground));
            Assert.That(cell.Background, Is.EqualTo(DefaultBackground));
            Assert.That(cell.Inverse, Is.False);
        });
    }

    /// <summary>
    /// Повторный проход по уже правленому экрану ничего не сообщает: иначе хост
    /// перестраивал бы экран на каждой порции вывода без причины.
    /// </summary>
    [Test]
    public void Normalize_SecondPass_ReportsNoChange()
    {
        var (model, video) = Screen();

        model.Feed("\u001b[7mr\u001b[27m");
        video.Normalize();

        Assert.That(video.Normalize(), Is.False);
    }

    /// <summary>
    /// Правка доходит до нижней строки экрана — там агенты и держат ввод.
    /// </summary>
    [Test]
    public void Normalize_LastVisibleRow_IsCovered()
    {
        var (model, video) = Screen();

        model.Feed(new string('\n', Rows - 1) + "\u001b[7mr\u001b[27m");

        Assert.That(video.Normalize(), Is.True);
        Assert.That(CellAt(model, Rows - 1, 0).Background, Is.EqualTo(0));
    }

    /// <summary>
    /// Пока пользователь смотрит прокрутку, процесс пишет всё равно в активный
    /// экран — правка идёт туда же. Считай она от видимой области, каретка в
    /// уехавшем вниз выводе осталась бы невидимой навсегда: следующий проход
    /// смотрит только вперёд.
    /// </summary>
    [Test]
    public void Normalize_WhileScrolledUp_CoversActiveScreen()
    {
        var (model, video) = Screen();

        model.Feed(new string('\n', Rows + 10));
        video.Normalize();
        model.ScrollLines(-10);

        Assert.That(model.Terminal.Buffer.IsAtBottom, Is.False, "вид уехал в прокрутку");

        model.Feed("\u001b[7mr\u001b[27m");

        Assert.That(video.Normalize(), Is.True);

        var buffer = model.Terminal.Buffer;

        Assert.That(CellAt(model, buffer.Y, buffer.X - 1).Background, Is.EqualTo(0));
    }

    /// <summary>
    /// Инверсия, уехавшая в прокрутку внутри одной порции вывода, тоже
    /// правится: болтливый процесс успевает прокрутить экран целиком между
    /// проходами, и в истории остался бы сырой вывод.
    /// </summary>
    [Test]
    public void Normalize_ScrolledPastBetweenPasses_IsCovered()
    {
        var (model, video) = Screen();

        model.Feed("\u001b[7mr\u001b[27m" + new string('\n', Rows + 10));

        var row = InverseRow(model);

        Assert.That(row, Is.LessThan(model.Terminal.Buffer.YBase), "инверсия уехала в прокрутку");
        Assert.That(video.Normalize(), Is.True);
        Assert.That(AbsoluteCellAt(model, row, 0).Background, Is.EqualTo(0));
    }

    /// <summary>
    /// Правка догоняет вывод и на заполненной прокрутке: там новая строка уже не
    /// поднимает YBase, и рост буфера виден только по вытеснению старых строк.
    /// </summary>
    [Test]
    public void Normalize_ScrolledPastOnFullScrollback_IsCovered()
    {
        const int scrollback = 8;
        var (model, video) = Screen(scrollback);

        // Сначала прокрутка заполняется до предела, дальше буфер начинает
        // вытеснять строки, и YBase стоит на месте.
        model.Feed(new string('\n', Rows + scrollback + 10));
        video.Normalize();

        var settledBase = model.Terminal.Buffer.YBase;

        Assert.That(settledBase, Is.EqualTo(scrollback), "YBase упёрся в размер прокрутки");

        model.Feed("\u001b[7mr\u001b[27m" + new string('\n', Rows + 5));

        var row = InverseRow(model);

        Assert.Multiple(() =>
        {
            Assert.That(model.Terminal.Buffer.YBase, Is.EqualTo(settledBase), "YBase больше не растёт");
            Assert.That(row, Is.LessThan(settledBase), "инверсия уехала в прокрутку");
        });

        Assert.That(video.Normalize(), Is.True);
        Assert.That(AbsoluteCellAt(model, row, 0).Background, Is.EqualTo(0));
    }

    /// <summary>
    /// Создаёт экранную модель 80×24 вместе с правщиком инверсии.
    /// </summary>
    /// <param name="scrollback">
    /// Глубина прокрутки.
    /// </param>
    /// <returns>
    /// Модель и правщик, созданный до первого вывода.
    /// </returns>
    private static (TerminalControlModel Model, InverseVideo Video) Screen(int scrollback = 200)
    {
        var model = new TerminalControlModel(new TerminalOptions
        {
            Cols = 80,
            Rows = Rows,
            Scrollback = scrollback,
            ConvertEol = true,
        });

        return (model, new InverseVideo(model));
    }

    /// <summary>
    /// Возвращает цвета и признак инверсии ячейки активного экрана.
    /// </summary>
    /// <param name="model">
    /// Экранная модель терминала.
    /// </param>
    /// <param name="row">
    /// Строка активного экрана.
    /// </param>
    /// <param name="column">
    /// Столбец.
    /// </param>
    /// <returns>
    /// Коды цветов текста и фона вместе с признаком инверсии.
    /// </returns>
    private static (int Foreground, int Background, bool Inverse) CellAt(TerminalControlModel model, int row, int column)
        => AbsoluteCellAt(model, model.Terminal.Buffer.YBase + row, column);

    /// <summary>
    /// Находит строку буфера с единственной инверсной ячейкой.
    /// </summary>
    /// <param name="model">
    /// Экранная модель терминала.
    /// </param>
    /// <returns>
    /// Номер строки в буфере вместе с прокруткой.
    /// </returns>
    private static int InverseRow(TerminalControlModel model)
    {
        var buffer = model.Terminal.Buffer;
        var found = new List<int>();

        for (var index = 0; index < buffer.Lines.Length; index++)
        {
            var line = buffer.GetLine(index);

            for (var column = 0; line is not null && column < line.Length; column++)
            {
                if (line[column].Attributes.IsInverse())
                {
                    found.Add(index);
                    break;
                }
            }
        }

        Assert.That(found, Has.Count.EqualTo(1), "в буфере должна быть ровно одна инверсная строка");
        return found[0];
    }

    /// <summary>
    /// Возвращает цвета и признак инверсии ячейки по номеру строки в буфере.
    /// </summary>
    /// <param name="model">
    /// Экранная модель терминала.
    /// </param>
    /// <param name="index">
    /// Номер строки в буфере вместе с прокруткой.
    /// </param>
    /// <param name="column">
    /// Столбец.
    /// </param>
    /// <returns>
    /// Коды цветов текста и фона вместе с признаком инверсии.
    /// </returns>
    private static (int Foreground, int Background, bool Inverse) AbsoluteCellAt(TerminalControlModel model, int index, int column)
    {
        var line = model.Terminal.Buffer.GetLine(index);

        Assert.That(line, Is.Not.Null, "строка буфера должна существовать");

        var attributes = line![column].Attributes;
        return (attributes.GetFgColor(), attributes.GetBgColor(), attributes.IsInverse());
    }
}
