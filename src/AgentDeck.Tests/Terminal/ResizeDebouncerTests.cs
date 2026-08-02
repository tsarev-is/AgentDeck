using AgentDeck.Terminal;
using NUnit.Framework;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Debounce ресайзов на управляемых часах.
/// </summary>
[TestFixture]
public class ResizeDebouncerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(100);

    private DateTimeOffset _now;
    private List<(int Cols, int Rows)> _applied = null!;

    [SetUp]
    public void SetUp()
    {
        _now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _applied = [];
    }

    /// <summary>
    /// Пачка ресайзов схлопывается в одну отправку последнего размера.
    /// </summary>
    [Test]
    public void Burst_CollapsesToSingleTrailingCall()
    {
        var debouncer = Create();

        for (var i = 0; i < 20; i++)
        {
            debouncer.Request(80 + i, 24 + i);
            Advance(TimeSpan.FromMilliseconds(10));
            debouncer.Tick();
        }

        Assert.That(_applied, Is.Empty, "внутри окна отправок быть не должно");

        Advance(Delay);
        debouncer.Tick();

        Assert.That(_applied, Is.EqualTo(new[] { (99, 43) }), "должен уйти только последний размер");
    }

    /// <summary>
    /// Разнесённые во времени ресайзы проходят все.
    /// </summary>
    [Test]
    public void SpacedRequests_AllPassThrough()
    {
        var debouncer = Create();

        foreach (var size in new[] { (100, 30), (120, 40), (90, 20) })
        {
            debouncer.Request(size.Item1, size.Item2);
            Advance(Delay + TimeSpan.FromMilliseconds(1));
            debouncer.Tick();
        }

        Assert.That(_applied, Is.EqualTo(new[] { (100, 30), (120, 40), (90, 20) }));
    }

    /// <summary>
    /// Тик до истечения окна ничего не отправляет.
    /// </summary>
    [Test]
    public void Tick_BeforeDelay_DoesNothing()
    {
        var debouncer = Create();
        debouncer.Request(100, 30);

        Advance(TimeSpan.FromMilliseconds(99));

        Assert.Multiple(() =>
        {
            Assert.That(debouncer.Tick(), Is.False);
            Assert.That(_applied, Is.Empty);
            Assert.That(debouncer.HasWork, Is.True);
        });
    }

    /// <summary>
    /// Повторный запрос того же размера не порождает лишней отправки.
    /// </summary>
    [Test]
    public void RepeatedSameSize_IsNotResent()
    {
        var debouncer = Create();

        debouncer.Request(100, 30);
        Advance(Delay);
        debouncer.Tick();

        debouncer.Request(100, 30);
        Advance(Delay);
        debouncer.Tick();

        Assert.That(_applied, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Flush отправляет отложенный размер немедленно.
    /// </summary>
    [Test]
    public void Flush_SendsPendingImmediately()
    {
        var debouncer = Create();
        debouncer.Request(111, 33);

        Assert.Multiple(() =>
        {
            Assert.That(debouncer.Flush(), Is.True);
            Assert.That(_applied, Is.EqualTo(new[] { (111, 33) }));
            Assert.That(debouncer.Flush(), Is.False, "второй flush уже нечего отправлять");
        });
    }

    /// <summary>
    /// Нулевые и отрицательные размеры игнорируются.
    /// </summary>
    [TestCase(0, 24)]
    [TestCase(80, 0)]
    [TestCase(-1, -1)]
    public void Request_NonPositiveSize_IsIgnored(int cols, int rows)
    {
        var debouncer = Create();

        debouncer.Request(cols, rows);
        Advance(Delay);
        debouncer.Tick();

        Assert.That(_applied, Is.Empty);
    }

    /// <summary>
    /// Режим подтверждений (ConPTY) повторяет последний размер после стабилизации.
    /// </summary>
    [Test]
    public void Confirmations_ResendLastSizeAfterSettling()
    {
        var debouncer = Create(confirmations: 1);

        debouncer.Request(120, 40);
        Advance(Delay);
        debouncer.Tick();

        Assert.That(_applied, Is.EqualTo(new[] { (120, 40) }));

        Advance(Delay);
        debouncer.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(_applied, Is.EqualTo(new[] { (120, 40), (120, 40) }), "нужен один повтор");
            Assert.That(debouncer.HasWork, Is.False);
        });

        Advance(Delay * 5);
        debouncer.Tick();
        Assert.That(_applied, Has.Count.EqualTo(2), "повтор ровно один");
    }

    /// <summary>
    /// Последний отправленный размер доступен для повторной отправки.
    /// </summary>
    [Test]
    public void LastApplied_TracksSentSize()
    {
        var debouncer = Create();

        Assert.That(debouncer.LastApplied, Is.Null);

        debouncer.Request(64, 16);
        Advance(Delay);
        debouncer.Tick();

        Assert.That(debouncer.LastApplied, Is.EqualTo((64, 16)));
    }

    /// <summary>
    /// Запросы идут из UI-потока, тики — с таймера. В PTY не должен уехать
    /// размер, собранный из двух разных запросов: колонки одного, строки другого.
    /// </summary>
    [Test]
    public void RequestAndTickConcurrently_NeverSendTornSize()
    {
        // Объём подобран по репро незалоченной версии: она рвала размер примерно
        // раз на шесть тысяч отправок, на этом числе промах практически исключён.
        const int iterations = 1_000_000;

        var torn = 0;
        var sent = 0;

        // Инвариант размера: rows = cols * 2. Его нарушение и означает разрыв.
        var debouncer = new ResizeDebouncer(
            (cols, rows) =>
            {
                Interlocked.Increment(ref sent);

                if (rows != cols * 2)
                {
                    Interlocked.Increment(ref torn);
                }
            },
            TimeSpan.Zero,
            () => DateTimeOffset.UtcNow);

        var ticking = true;

        var ticker = Task.Run(() =>
        {
            while (Volatile.Read(ref ticking))
            {
                debouncer.Tick();
            }
        });

        for (var i = 1; i <= iterations; i++)
        {
            var cols = 20 + (i % 300);
            debouncer.Request(cols, cols * 2);
        }

        Volatile.Write(ref ticking, false);
        Assert.That(ticker.Wait(TimeSpan.FromSeconds(10)), Is.True);

        debouncer.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(torn, Is.Zero, "размер должен уходить в PTY только целиком");
            Assert.That(sent, Is.GreaterThan(0), "тики должны были что-то отправить");
        });
    }

    private ResizeDebouncer Create(int confirmations = 0)
        => new((cols, rows) => _applied.Add((cols, rows)), Delay, () => _now, confirmations);

    private void Advance(TimeSpan span) => _now += span;
}
