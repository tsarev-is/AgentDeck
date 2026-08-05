namespace AgentDeck.Terminal;

/// <summary>
/// Перевод колеса мыши в нажатия стрелок для приложений на альтернативном экране.
/// </summary>
/// <remarks>
/// У альтернативного экрана прокрутки нет и быть не может: приложение рисует его
/// целиком и заново, и выше видимых строк у терминала ничего не остаётся —
/// полоса прокрутки в таком тайле пуста, а колесо двигать нечего. Настоящие
/// терминалы (xterm с alternateScroll, kitty, iTerm2, VTE) в этом случае
/// превращают колесо в нажатия стрелок; именно так прокручиваются less, man,
/// <c>git log</c> и vim без мыши.
/// Приложения, которые следят за мышью сами (claude), в этот перевод попадать не
/// должны: колесо им и так доходит событиями мыши, а стрелки увели бы их по
/// истории ввода вместо прокрутки.
/// </remarks>
public static class AlternateScroll
{
    /// <summary>
    /// Стрелка вверх в обычном режиме курсорных клавиш.
    /// </summary>
    private const string Up = "\u001b[A";

    /// <summary>
    /// Стрелка вниз в обычном режиме курсорных клавиш.
    /// </summary>
    private const string Down = "\u001b[B";

    /// <summary>
    /// Стрелка вверх в прикладном режиме курсорных клавиш (DECCKM).
    /// </summary>
    private const string ApplicationUp = "\u001bOA";

    /// <summary>
    /// Стрелка вниз в прикладном режиме курсорных клавиш (DECCKM).
    /// </summary>
    private const string ApplicationDown = "\u001bOB";

    /// <summary>
    /// Собирает нажатия стрелок, которыми колесо прокручивает полноэкранное
    /// приложение.
    /// </summary>
    /// <param name="delta">
    /// Вертикальная составляющая поворота колеса: положительная — от себя, к
    /// более раннему выводу.
    /// </param>
    /// <param name="rows">
    /// Высота экрана тайла — больше неё за один поворот не прокручиваем.
    /// </param>
    /// <param name="applicationCursorKeys">
    /// Приложение включило прикладной режим курсорных клавиш (DECCKM).
    /// </param>
    /// <returns>
    /// Последовательность для ввода процесса; пустая строка, если колесо не
    /// повернулось.
    /// </returns>
    public static string Keys(double delta, int rows, bool applicationCursorKeys)
    {
        if (delta is 0 || double.IsNaN(delta))
        {
            return string.Empty;
        }

        var key = delta > 0
            ? applicationCursorKeys ? ApplicationUp : Up
            : applicationCursorKeys ? ApplicationDown : Down;

        return string.Concat(Enumerable.Repeat(key, Lines(Math.Abs(delta), rows)));
    }

    /// <summary>
    /// Считает, на сколько строк прокрутить один поворот колеса.
    /// </summary>
    /// <param name="delta">
    /// Модуль вертикальной составляющей поворота.
    /// </param>
    /// <param name="rows">
    /// Высота экрана тайла.
    /// </param>
    /// <remarks>
    /// Шаг повторяет собственную прокрутку контрола, иначе колесо ощущалось бы
    /// по-разному в тайлах с прокруткой и без неё. Экраном шаг ограничен: в
    /// низком тайле «страница» стрелок пролетела бы весь вывод разом.
    /// </remarks>
    private static int Lines(double delta, int rows) => Math.Clamp(
        delta switch
        {
            > 9 => rows,
            > 5 => 10,
            > 1 => 3,
            _ => 1,
        },
        1,
        Math.Max(rows, 1));
}
