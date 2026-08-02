using AgentDeck.Models;
using AgentDeck.Status;
using NUnit.Framework;

namespace AgentDeck.Tests.Status;

/// <summary>
/// Детектор статусов на управляемых часах: слои приоритетов и debounce.
/// </summary>
[TestFixture]
public class AgentStatusDetectorTests
{
    private static readonly TimeSpan Persistence = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(500);

    private static readonly string[] Quiet = ["$ ", ""];
    private static readonly string[] PermissionRows = ["│ Do you want to proceed?", "│ ❯ 1. Yes"];
    private static readonly string[] BusyRows = ["✻ Scurrying… (4s)"];

    private DateTimeOffset _now;
    private long _counter;

    [SetUp]
    public void SetUp()
    {
        _now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _counter = 0;
    }

    /// <summary>
    /// Нулевой код возврата даёт Finished и перекрывает любые паттерны.
    /// </summary>
    [Test]
    public void ExitCodeZero_YieldsFinished()
    {
        var detector = Create();

        Assert.That(detector.Update(new StatusSnapshot(PermissionRows, _counter, true, 0)), Is.EqualTo(TileStatus.Finished));
    }

    /// <summary>
    /// Ненулевой код возврата даёт Crashed и перекрывает любые паттерны.
    /// </summary>
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(130)]
    public void NonZeroExitCode_YieldsCrashed(int exitCode)
    {
        var detector = Create();

        Assert.That(
            detector.Update(new StatusSnapshot(BusyRows, _counter, true, exitCode)),
            Is.EqualTo(TileStatus.Crashed));
    }

    /// <summary>
    /// Паттерн, увиденный один тик, статус не переключает.
    /// </summary>
    [Test]
    public void PermissionPattern_SingleTick_DoesNotFlipStatus()
    {
        var detector = Create();
        Advance(Tick);

        var status = detector.Update(Snapshot(PermissionRows, changed: true));

        Assert.That(status, Is.Not.EqualTo(TileStatus.AwaitingPermission), "одного тика недостаточно");
    }

    /// <summary>
    /// Паттерн, устоявшийся дольше debounce, переключает в AwaitingPermission.
    /// </summary>
    [Test]
    public void PermissionPattern_StableBeyondDebounce_YieldsAwaitingPermission()
    {
        var detector = Create();
        var status = TileStatus.Running;

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            status = detector.Update(Snapshot(PermissionRows, changed: false));
        }

        Assert.That(status, Is.EqualTo(TileStatus.AwaitingPermission));
    }

    /// <summary>
    /// Мерцание паттерна между тиками статус не переключает.
    /// </summary>
    [Test]
    public void FlickeringPattern_NeverConfirms()
    {
        var detector = Create();

        for (var i = 0; i < 10; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(i % 2 == 0 ? PermissionRows : Quiet, changed: true));
        }

        Assert.That(detector.Status, Is.Not.EqualTo(TileStatus.AwaitingPermission), "мерцание не должно подтверждаться");
    }

    /// <summary>
    /// Исчезновение паттерна после debounce возвращает статус по активности буфера.
    /// </summary>
    [Test]
    public void PermissionPattern_Disappears_RevertsToActivityStatus()
    {
        var detector = Create();

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(PermissionRows, changed: false));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.AwaitingPermission));

        // Пользователь ответил: паттерн ушёл, вывод пошёл.
        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(Quiet, changed: true));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.Running));
    }

    /// <summary>
    /// Растущий счётчик изменений держит статус Running.
    /// </summary>
    [Test]
    public void GrowingChangeCounter_YieldsRunning()
    {
        var detector = Create();

        for (var i = 0; i < 6; i++)
        {
            Advance(Tick);
            Assert.That(detector.Update(Snapshot(Quiet, changed: true)), Is.EqualTo(TileStatus.Running));
        }
    }

    /// <summary>
    /// Замерший буфер дольше порога простоя даёт AwaitingInput.
    /// </summary>
    [Test]
    public void FrozenBuffer_BeyondIdleThreshold_YieldsAwaitingInput()
    {
        var detector = Create();
        detector.Update(Snapshot(Quiet, changed: true));

        Advance(IdleAfter + Tick);

        Assert.That(detector.Update(Snapshot(Quiet, changed: false)), Is.EqualTo(TileStatus.AwaitingInput));
    }

    /// <summary>
    /// Буфер, замерший меньше порога, остаётся в Running.
    /// </summary>
    [Test]
    public void FrozenBuffer_BelowIdleThreshold_StaysRunning()
    {
        var detector = Create();
        detector.Update(Snapshot(Quiet, changed: true));

        Advance(TimeSpan.FromMilliseconds(1500));

        Assert.That(detector.Update(Snapshot(Quiet, changed: false)), Is.EqualTo(TileStatus.Running));
    }

    /// <summary>
    /// Маркер занятости удерживает Running при полностью замершем буфере.
    /// </summary>
    [Test]
    public void BusyMarker_HoldsRunning_WhileBufferIsFrozen()
    {
        var detector = Create();

        // Буфер не меняется, но индикатор занятости висит на экране.
        for (var i = 0; i < 12; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(BusyRows, changed: false));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.Running), "маркер занятости должен перебивать простой");
    }

    /// <summary>
    /// Без маркера тот же замерший буфер уходит в AwaitingInput —
    /// подтверждает, что предыдущий тест проверяет именно маркер.
    /// </summary>
    [Test]
    public void WithoutBusyMarker_FrozenBufferGoesIdle()
    {
        var detector = Create();

        for (var i = 0; i < 12; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(Quiet, changed: false));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.AwaitingInput));
    }

    /// <summary>
    /// Подтверждение приоритетнее занятости и на уровне детектора.
    /// </summary>
    [Test]
    public void PermissionOverBusy_YieldsAwaitingPermission()
    {
        var detector = Create();
        string[] both = ["✻ Scurrying… (9s)", "│ Do you want to proceed?"];

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(both, changed: true));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.AwaitingPermission));
    }

    /// <summary>
    /// Завершение процесса перекрывает подтверждённое ожидание разрешения.
    /// </summary>
    [Test]
    public void Exit_OverridesConfirmedPermission()
    {
        var detector = Create();

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(PermissionRows, changed: false));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.AwaitingPermission));

        Advance(Tick);

        Assert.That(
            detector.Update(new StatusSnapshot(PermissionRows, _counter, true, 1)),
            Is.EqualTo(TileStatus.Crashed));
    }

    /// <summary>
    /// Для «script» паттерны не работают: остаются код возврата и активность.
    /// </summary>
    [Test]
    public void Script_IgnoresPatterns_AndUsesActivityOnly()
    {
        var detector = Create(AgentKind.Script);

        for (var i = 0; i < 12; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(PermissionRows, changed: false));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.AwaitingInput), "у script нет паттернов подтверждения");
    }

    /// <summary>
    /// Свежесозданный детектор считает процесс работающим.
    /// </summary>
    [Test]
    public void NewDetector_StartsRunning()
    {
        Assert.That(Create().Status, Is.EqualTo(TileStatus.Running));
    }

    private AgentStatusDetector Create(AgentKind kind = AgentKind.Claude)
        => new(kind, () => _now, Persistence, IdleAfter);

    private StatusSnapshot Snapshot(IReadOnlyList<string> rows, bool changed)
    {
        if (changed)
        {
            _counter++;
        }

        return new StatusSnapshot(rows, _counter, false, null);
    }

    private void Advance(TimeSpan span) => _now += span;
}
