using AgentDeck.Terminal;
using NUnit.Framework;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Перевод колеса мыши в стрелки на альтернативном экране: своей прокрутки у
/// такого экрана нет, и без перевода колесо в тайле не делает ничего.
/// </summary>
[TestFixture]
public class AlternateScrollTests
{
    /// <summary>
    /// Высота экрана в тестах.
    /// </summary>
    private const int Rows = 24;

    /// <summary>
    /// Колесо от себя прокручивает к более раннему выводу — это стрелка вверх.
    /// </summary>
    [Test]
    public void Keys_WheelUp_RepeatsCursorUp()
    {
        Assert.That(AlternateScroll.Keys(1, Rows, applicationCursorKeys: false), Is.EqualTo("\u001b[A"));
    }

    /// <summary>
    /// Колесо к себе — стрелка вниз.
    /// </summary>
    [Test]
    public void Keys_WheelDown_RepeatsCursorDown()
    {
        Assert.That(AlternateScroll.Keys(-1, Rows, applicationCursorKeys: false), Is.EqualTo("\u001b[B"));
    }

    /// <summary>
    /// В прикладном режиме курсорных клавиш стрелки идут в форме SS3: именно его
    /// включают less, man и <c>git log</c>, и обычную форму они не поймут.
    /// </summary>
    [Test]
    public void Keys_ApplicationCursorKeys_UsesSs3Form()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlternateScroll.Keys(1, Rows, applicationCursorKeys: true), Is.EqualTo("\u001bOA"));
            Assert.That(AlternateScroll.Keys(-1, Rows, applicationCursorKeys: true), Is.EqualTo("\u001bOB"));
        });
    }

    /// <summary>
    /// Шаг колеса растёт вместе с поворотом — так же, как в собственной прокрутке
    /// контрола.
    /// </summary>
    [Test]
    public void Keys_LargerDelta_ScrollsMoreLines()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlternateScroll.Keys(3, Rows, applicationCursorKeys: false), Is.EqualTo(string.Concat(Enumerable.Repeat("\u001b[A", 3))));
            Assert.That(AlternateScroll.Keys(7, Rows, applicationCursorKeys: false), Is.EqualTo(string.Concat(Enumerable.Repeat("\u001b[A", 10))));
        });
    }

    /// <summary>
    /// Шаг ограничен экраном: в низком тайле «страница» стрелок пролетела бы весь
    /// вывод разом.
    /// </summary>
    [Test]
    public void Keys_ShortTile_ClampsToScreenHeight()
    {
        Assert.That(AlternateScroll.Keys(20, 5, applicationCursorKeys: false), Is.EqualTo(string.Concat(Enumerable.Repeat("\u001b[A", 5))));
    }

    /// <summary>
    /// Горизонтальный поворот прокруткой не считается.
    /// </summary>
    [Test]
    public void Keys_NoVerticalDelta_ReturnsEmpty()
    {
        Assert.That(AlternateScroll.Keys(0, Rows, applicationCursorKeys: false), Is.EqualTo(string.Empty));
    }
}
