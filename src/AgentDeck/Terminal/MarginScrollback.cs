using SvcSystems.UI.Terminal;

namespace AgentDeck.Terminal;

/// <summary>
/// Сохраняет в прокрутке строки, уходящие вверх из региона прокрутки, прижатого к верху экрана.
/// </summary>
public sealed class MarginScrollback
{
    /// <summary>
    /// Состояние разбора escape-последовательностей — ровно в той полноте, в
    /// какой нужно отличить исполняемый перевод строки от байта внутри
    /// последовательности.
    /// </summary>
    private enum ParserState
    {
        /// <summary>
        /// Обычный текст.
        /// </summary>
        Ground,

        /// <summary>
        /// Встретился ESC, тип последовательности ещё не известен.
        /// </summary>
        Escape,

        /// <summary>
        /// Внутри CSI-последовательности (<c>ESC[</c>).
        /// </summary>
        Csi,

        /// <summary>
        /// Внутри строковой последовательности (OSC, DCS, APC): управляющие
        /// символы в ней не исполняются.
        /// </summary>
        String,
    }

    /// <summary>
    /// Что в потоке прокручивает регион.
    /// </summary>
    private enum ScrollTrigger
    {
        /// <summary>
        /// Ничего.
        /// </summary>
        None,

        /// <summary>
        /// Перевод строки (LF, VT, FF): прокручивает, только если курсор стоит
        /// на нижней границе региона.
        /// </summary>
        LineFeed,

        /// <summary>
        /// <c>ESC D</c> (IND): тот же перевод строки, но столбец курсора
        /// сохраняется.
        /// </summary>
        Index,

        /// <summary>
        /// <c>ESC E</c> (NEL): перевод строки вместе с возвратом каретки.
        /// </summary>
        NextLine,

        /// <summary>
        /// <c>ESC[nS</c> (SU): прокручивает регион независимо от курсора.
        /// </summary>
        ScrollUp,
    }

    private const byte Bell = 0x07;
    private const byte LineFeed = 0x0a;
    private const byte VerticalTab = 0x0b;
    private const byte FormFeed = 0x0c;
    private const byte Escape = 0x1b;

    /// <summary>
    /// Предел длины задержанной последовательности: осмысленные короче, а на
    /// мусоре в потоке ждать её конца нельзя.
    /// </summary>
    private const int MaxHeldSequence = 32;

    private readonly TerminalControlModel _model;

    private byte[] _held = [];
    private ParserState _state = ParserState.Ground;
    private int _sequenceStart = -1;
    private int _parameter = -1;
    private bool _firstParameter = true;
    private bool _plainCsi = true;
    private bool _privateCsi;
    private bool _regionChanged;
    private bool _regionActive;

    /// <summary>
    /// Создаёт правщик прокрутки для экранной модели.
    /// </summary>
    /// <param name="model">
    /// Экранная модель терминала.
    /// </param>
    public MarginScrollback(TerminalControlModel model) => _model = model;

    /// <summary>
    /// Отдаёт порцию вывода в разбор, сохраняя в прокрутке строки, которые иначе
    /// пропали бы вверху региона.
    /// </summary>
    /// <param name="chunk">
    /// Порция вывода процесса.
    /// </param>
    /// <param name="length">
    /// Число значащих байт в порции.
    /// </param>
    public void Feed(byte[] chunk, int length)
    {
        if (_held.Length > 0)
        {
            // Задержанное начало последовательности возвращается в поток: теперь
            // её видно целиком, и вырезать её можно вместе с ESC.
            chunk = [.. _held, .. chunk.AsSpan(0, length)];
            length = chunk.Length;
            _held = [];
        }
        else if (_state is not ParserState.Ground)
        {
            // Последовательность оказалась длиннее предела и уже разобрана — её
            // начало в этой порции не найти.
            _sequenceStart = -1;
        }

        var start = 0;
        var touched = false;

        for (var index = 0; index < length; index++)
        {
            var trigger = Step(chunk[index], index, out var lines);

            if (trigger is ScrollTrigger.None)
            {
                continue;
            }

            // Дешёвый отказ: пока в потоке не было ни DECSTBM, ни переключения
            // экрана, регион остаётся тем, каким его прочитали в прошлый раз, и
            // резать поток незачем.
            if (!_regionActive && !_regionChanged)
            {
                continue;
            }

            // Перевод строки — один байт, у остальных перед последним байтом
            // стоит ESC: вырезать нужно последовательность целиком.
            var sequence = trigger is ScrollTrigger.LineFeed ? index : _sequenceStart;

            if (sequence < start)
            {
                continue;
            }

            // Границы региона и курсор нужны такими, какими они будут к этому
            // месту потока, — поэтому предыдущие байты сначала разбираются.
            touched |= FeedSlice(chunk, start, sequence - start);
            _regionChanged = false;
            _regionActive = IsRegionAnchored();
            start = sequence;

            if (!_regionActive || (trigger is not ScrollTrigger.ScrollUp && !IsCursorAtBottomMargin()))
            {
                continue;
            }

            ScrollWithScrollback(lines);
            MoveCursorAfterScroll(trigger);

            start = index + 1;
            touched = true;
        }

        var tail = HoldUnfinishedSequence(chunk, start, length);

        touched |= FeedSlice(chunk, start, tail - start);

        // Экран собирается мимо экранной модели, поэтому обновление у порции
        // одно — и на каждый перевод строки видимая область не перестраивается.
        if (touched)
        {
            _model.UpdateDisplay();
        }
    }

    /// <summary>
    /// Сбрасывает разбор перед перезапуском процесса в том же тайле.
    /// </summary>
    public void Reset()
    {
        _held = [];
        _state = ParserState.Ground;
        _sequenceStart = -1;
        _parameter = -1;
        _firstParameter = true;
        _plainCsi = true;
        _privateCsi = false;
        _regionChanged = false;
        _regionActive = false;
    }

    /// <summary>
    /// Задерживает незакрытый хвост порции — начало escape-последовательности,
    /// конец которой придёт следующей порцией.
    /// </summary>
    /// <param name="chunk">
    /// Порция вывода процесса.
    /// </param>
    /// <param name="start">
    /// Начало неразобранного остатка порции.
    /// </param>
    /// <param name="length">
    /// Число значащих байт в порции.
    /// </param>
    /// <returns>
    /// Граница, до которой остаток можно разбирать.
    /// </returns>
    /// <remarks>
    /// До последнего байта последовательности разбор всё равно ничего не делает,
    /// поэтому задержка на экране не видна. Зато прокрутку, разрезанную границей
    /// порций, иначе было бы нечем перехватить: её начало уже ушло в разбор, и
    /// вырезать её целиком не получилось бы.
    /// </remarks>
    private int HoldUnfinishedSequence(byte[] chunk, int start, int length)
    {
        if (_state is not (ParserState.Escape or ParserState.Csi)
            || _sequenceStart < start
            || length - _sequenceStart > MaxHeldSequence)
        {
            return length;
        }

        var held = _sequenceStart;
        _held = chunk[held..length];

        // Задержанные байты будут разобраны заново — вместе с состоянием.
        _state = ParserState.Ground;
        _sequenceStart = -1;

        return held;
    }

    /// <summary>
    /// Прокручивает регион вверх, отдавая ушедшие строки в прокрутку.
    /// </summary>
    /// <param name="lines">
    /// На сколько строк прокрутить.
    /// </param>
    /// <remarks>
    /// Прокрутка региона <c>[0, b]</c> на строку равна прокрутке всего экрана
    /// (она-то и пополняет прокрутку) с последующим сдвигом вниз региона
    /// <c>[b, низ экрана]</c>: первый шаг уносит верхнюю строку в прокрутку и
    /// поднимает весь экран, второй возвращает на место строки под регионом и
    /// оставляет пустую строку на нижней границе — ровно то, что должен сделать
    /// перевод строки на границе.
    /// </remarks>
    private void ScrollWithScrollback(int lines)
    {
        var buffer = _model.Terminal.Buffer;
        var bottom = buffer.ScrollBottom;
        var last = buffer.Rows - 1;

        // Из региона может уйти вверх только то, что в нём есть: настоящий
        // терминал на «ESC[999S» в четырёхстрочном регионе пополняет прокрутку
        // четырьмя строками, а не высотой экрана — остальное было бы пустотой, и
        // на столько же прыгнули бы вид и полоса прокрутки.
        var count = Math.Min(lines, bottom - buffer.ScrollTop + 1);

        for (var line = 0; line < count; line++)
        {
            buffer.SetScrollRegion(0, last);
            buffer.ScrollUp(1);
            buffer.SetScrollRegion(bottom, last);
            buffer.ScrollDown(1);
        }

        buffer.SetScrollRegion(0, bottom);
    }

    /// <summary>
    /// Доводит курсор до состояния после прокрутки на нижней границе региона:
    /// строка не меняется, а столбец встаёт ровно туда, куда его поставил бы
    /// разбор.
    /// </summary>
    /// <param name="trigger">
    /// Что прокрутило регион.
    /// </param>
    /// <remarks>
    /// NEL несёт возврат каретки в себе, перевод строки — только когда движок
    /// дописывает его сам, а IND и SU курсор по строке не двигают.
    /// </remarks>
    private void MoveCursorAfterScroll(ScrollTrigger trigger)
    {
        var terminal = _model.Terminal;

        var carriageReturn = trigger is ScrollTrigger.NextLine
            || (trigger is ScrollTrigger.LineFeed && terminal.Options.ConvertEol);

        if (carriageReturn)
        {
            terminal.Buffer.SetCursor(0, terminal.Buffer.Y);
        }
    }

    /// <summary>
    /// Регион прокрутки прижат к верху экрана и не занимает его целиком.
    /// </summary>
    /// <returns>
    /// true, если строки, уходящие вверх, должны попадать в прокрутку.
    /// </returns>
    /// <remarks>
    /// У альтернативного экрана прокрутки нет вовсе, и подменять его прокрутку
    /// региона нельзя: там она выполняется верно и без нас.
    /// </remarks>
    private bool IsRegionAnchored()
    {
        var terminal = _model.Terminal;
        var buffer = terminal.Buffer;

        return !terminal.IsAlternateBufferActive
            && buffer.ScrollTop == 0
            && buffer.ScrollBottom < buffer.Rows - 1;
    }

    /// <summary>
    /// Курсор стоит на нижней границе региона — перевод строки прокрутит его.
    /// </summary>
    private bool IsCursorAtBottomMargin()
    {
        var buffer = _model.Terminal.Buffer;
        return buffer.Y == buffer.ScrollBottom;
    }

    /// <summary>
    /// Отдаёт часть порции в разбор.
    /// </summary>
    /// <param name="chunk">
    /// Порция вывода процесса.
    /// </param>
    /// <param name="offset">
    /// Начало части.
    /// </param>
    /// <param name="count">
    /// Длина части.
    /// </param>
    /// <returns>
    /// false, если часть пустая и разбирать было нечего.
    /// </returns>
    /// <remarks>
    /// Разбор идёт в терминал, а не в экранную модель: та на каждую порцию
    /// перестраивает видимую область и дёргает UI, а порция здесь режется на
    /// каждом переводе строки — на болтливом выводе это давало сотни
    /// перестроек экрана вместо одной. Состояние декодера UTF-8 живёт в
    /// терминале, поэтому разрезанный поток он собирает как целый. Экран
    /// обновляется один раз, когда порция разобрана целиком.
    /// </remarks>
    private bool FeedSlice(byte[] chunk, int offset, int count)
    {
        if (count <= 0)
        {
            return false;
        }

        // Разрез приходится на границу символа: и перевод строки, и ESC —
        // однобайтовые, внутрь UTF-8 такой разрез не попадает.
        _model.Terminal.Feed(offset == 0 ? chunk : chunk[offset..(offset + count)], count);
        return true;
    }

    /// <summary>
    /// Продвигает разбор на байт и сообщает, прокручивает ли он регион.
    /// </summary>
    /// <param name="value">
    /// Байт вывода.
    /// </param>
    /// <param name="index">
    /// Его смещение в порции — начало последовательности запоминается, чтобы
    /// вырезать её целиком.
    /// </param>
    /// <param name="lines">
    /// На сколько строк прокручивает.
    /// </param>
    /// <returns>
    /// Что в потоке прокручивает регион.
    /// </returns>
    private ScrollTrigger Step(byte value, int index, out int lines)
    {
        lines = 1;

        if (value is Escape)
        {
            _state = ParserState.Escape;
            _sequenceStart = index;
            return ScrollTrigger.None;
        }

        if (value < 0x20)
        {
            return StepControl(value);
        }

        switch (_state)
        {
            case ParserState.Escape:
                return StepEscape(value);

            case ParserState.Csi:
                return StepCsi(value, out lines);

            default:
                return ScrollTrigger.None;
        }
    }

    /// <summary>
    /// Продвигает разбор на управляющем символе.
    /// </summary>
    /// <param name="value">
    /// Байт вывода меньше 0x20.
    /// </param>
    /// <returns>
    /// Что в потоке прокручивает регион.
    /// </returns>
    /// <remarks>
    /// Внутри строковой последовательности управляющие символы не исполняются
    /// (звонок её закрывает), в остальных состояниях исполняются сразу, не сбивая
    /// разбор, — так же, как в самом парсере.
    /// </remarks>
    private ScrollTrigger StepControl(byte value)
    {
        if (_state is ParserState.String)
        {
            if (value is Bell)
            {
                _state = ParserState.Ground;
            }

            return ScrollTrigger.None;
        }

        return value is LineFeed or VerticalTab or FormFeed
            ? ScrollTrigger.LineFeed
            : ScrollTrigger.None;
    }

    /// <summary>
    /// Определяет тип последовательности по байту после ESC и сообщает,
    /// прокручивает ли она регион.
    /// </summary>
    /// <param name="value">
    /// Байт после ESC.
    /// </param>
    /// <returns>
    /// Что в потоке прокручивает регион.
    /// </returns>
    /// <remarks>
    /// Перевод строки приходит и в этой форме: IND (<c>ESC D</c>) и NEL
    /// (<c>ESC E</c>) на нижней границе региона прокручивают его так же, как
    /// голый <c>\n</c>, и без них уходящая вверх строка теряется.
    /// </remarks>
    private ScrollTrigger StepEscape(byte value)
    {
        switch (value)
        {
            case (byte)'[':
                _state = ParserState.Csi;
                _parameter = -1;
                _firstParameter = true;
                _plainCsi = true;
                _privateCsi = false;
                return ScrollTrigger.None;

            case (byte)']':
            case (byte)'P':
            case (byte)'X':
            case (byte)'^':
            case (byte)'_':
                _state = ParserState.String;
                return ScrollTrigger.None;

            case (byte)'D':
                _state = ParserState.Ground;
                return ScrollTrigger.Index;

            case (byte)'E':
                _state = ParserState.Ground;
                return ScrollTrigger.NextLine;

            default:
                // Промежуточные байты продолжают последовательность, остальные её
                // завершают: и то и другое к прокрутке региона не относится.
                _state = value is >= 0x20 and <= 0x2f ? ParserState.Escape : ParserState.Ground;
                return ScrollTrigger.None;
        }
    }

    /// <summary>
    /// Продвигает разбор внутри CSI-последовательности.
    /// </summary>
    /// <param name="value">
    /// Байт внутри последовательности.
    /// </param>
    /// <param name="lines">
    /// На сколько строк прокручивает.
    /// </param>
    /// <returns>
    /// Что в потоке прокручивает регион.
    /// </returns>
    private ScrollTrigger StepCsi(byte value, out int lines)
    {
        lines = 1;

        switch (value)
        {
            case >= (byte)'0' and <= (byte)'9':
                if (_firstParameter)
                {
                    // Разбор с потолком: длинное число нужно лишь для того, чтобы
                    // не переполнить счётчик, прокрутка всё равно ограничена регионом.
                    _parameter = Math.Min(Math.Max(_parameter, 0) * 10 + (value - '0'), 100000);
                }

                return ScrollTrigger.None;

            case (byte)';':
                _firstParameter = false;
                return ScrollTrigger.None;

            case (byte)'?':
                // Приватный режим: это не SU и не DECSTBM, но DECSET/DECRST
                // умеют переключить экран — за такими последовательностями надо
                // проследить до конца.
                _plainCsi = false;
                _privateCsi = true;
                return ScrollTrigger.None;

            case >= 0x20 and <= 0x2f:
            case >= 0x3a and <= 0x3f:
                // Промежуточные байты и прочие приватные префиксы: это уже не SU
                // и не DECSTBM.
                _plainCsi = false;
                return ScrollTrigger.None;

            case >= 0x40 and <= 0x7e:
                return FinishCsi(value, out lines);

            default:
                return ScrollTrigger.None;
        }
    }

    /// <summary>
    /// Закрывает CSI-последовательность её последним байтом.
    /// </summary>
    /// <param name="value">
    /// Последний байт последовательности.
    /// </param>
    /// <param name="lines">
    /// На сколько строк прокручивает.
    /// </param>
    /// <returns>
    /// Что в потоке прокручивает регион.
    /// </returns>
    private ScrollTrigger FinishCsi(byte value, out int lines)
    {
        var parameter = _parameter;
        var plain = _plainCsi;
        var privateMode = _privateCsi;

        _state = ParserState.Ground;
        lines = 1;

        if (privateMode)
        {
            // Переключение экрана (DECSET/DECRST 1049, 1047, 47) переставляет и
            // границы региона: у альтернативного экрана они свои, а при возврате
            // прежние встают на место. Прочитанное до переключения больше не
            // годится — иначе поход процесса в пейджер оставил бы разбор при
            // мнении, что региона нет, и прокрутка тайла перестала бы
            // пополняться до следующего DECSTBM.
            if (value is (byte)'h' or (byte)'l' && parameter is 47 or 1047 or 1049)
            {
                _regionChanged = true;
            }

            return ScrollTrigger.None;
        }

        if (!plain)
        {
            return ScrollTrigger.None;
        }

        switch (value)
        {
            case (byte)'r':
                // DECSTBM переставил границы — прочитать их заново.
                _regionChanged = true;
                return ScrollTrigger.None;

            case (byte)'S':
                // Высотой региона прокрутка ограничивается там, где регион уже
                // прочитан: здесь его границы — от предыдущей порции.
                lines = Math.Max(parameter, 1);
                return ScrollTrigger.ScrollUp;

            default:
                return ScrollTrigger.None;
        }
    }
}
