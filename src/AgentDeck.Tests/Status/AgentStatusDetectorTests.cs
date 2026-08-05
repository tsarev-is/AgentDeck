using AgentDeck.Models;
using AgentDeck.Status;
using NUnit.Framework;

namespace AgentDeck.Tests.Status;

/// <summary>
/// Детектор статусов на управляемых часах: слои приоритетов и выдержка сигнала.
/// Статус тайла — это то, что показывает лампочка в заголовке: <c>Running</c> и
/// <c>AwaitingInput</c> горят ровно, <c>Working</c> мигает.
/// </summary>
[TestFixture]
public class AgentStatusDetectorTests
{
    private static readonly TimeSpan Persistence = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(500);

    private static readonly string[] Quiet = ["❯ ", ""];
    private static readonly string[] PermissionRows = ["│ Do you want to proceed?", "│ ❯ 1. Yes"];
    private static readonly string[] BusyRows = ["✻ Scurrying… (4s)"];

    private DateTimeOffset _now;

    [SetUp]
    public void SetUp() => _now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Нулевой код возврата даёт Finished и перекрывает любые паттерны.
    /// </summary>
    [Test]
    public void ExitCodeZero_YieldsFinished()
    {
        var detector = Create();

        Assert.That(detector.Update(new StatusSnapshot(PermissionRows, true, 0)), Is.EqualTo(TileStatus.Finished));
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
            detector.Update(new StatusSnapshot(BusyRows, true, exitCode)),
            Is.EqualTo(TileStatus.Crashed));
    }

    /// <summary>
    /// Маркер занятости даёт Working с первого же тика: короткий запрос успевает
    /// отработать за секунду, и выдержка на входе съела бы всё мигание.
    /// </summary>
    [Test]
    public void BusyMarker_YieldsWorkingImmediately()
    {
        var detector = Create();
        Advance(Tick);

        Assert.That(detector.Update(Snapshot(BusyRows)), Is.EqualTo(TileStatus.Working));
    }

    /// <summary>
    /// Пока маркер занятости на экране, статус остаётся Working, сколько бы это
    /// ни длилось: замерший буфер занятости не отменяет — модель может думать
    /// минуту, ничего не печатая.
    /// </summary>
    [Test]
    public void BusyMarker_HoldsWorking_WhileOnScreen()
    {
        var detector = Create();

        for (var i = 0; i < 12; i++)
        {
            Advance(Tick);
            Assert.That(detector.Update(Snapshot(BusyRows)), Is.EqualTo(TileStatus.Working));
        }
    }

    /// <summary>
    /// Пропажа маркера на один кадр статус не роняет: агент перерисовывает экран
    /// целиком, и снимок может попасть между стиранием строки и её отрисовкой.
    /// </summary>
    [Test]
    public void BusyMarker_SingleFrameGap_StaysWorking()
    {
        var detector = Create();

        Advance(Tick);
        detector.Update(Snapshot(BusyRows));

        Advance(Tick);
        detector.Update(Snapshot(Quiet));

        Advance(Tick);

        Assert.That(detector.Update(Snapshot(BusyRows)), Is.EqualTo(TileStatus.Working));
    }

    /// <summary>
    /// Маркер ушёл насовсем — запрос отработан, и лампочка перестаёт мигать.
    /// </summary>
    [Test]
    public void BusyMarker_GoneBeyondPersistence_YieldsAwaitingInput()
    {
        var detector = Create();

        Advance(Tick);

        Assert.That(detector.Update(Snapshot(BusyRows)), Is.EqualTo(TileStatus.Working));

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(Quiet));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.AwaitingInput));
    }

    /// <summary>
    /// Спокойный экран агента — ожидание ввода: лампочка горит ровно, а не мигает.
    /// </summary>
    [Test]
    public void QuietAgentScreen_YieldsAwaitingInput()
    {
        var detector = Create();
        Advance(Tick);

        Assert.That(detector.Update(Snapshot(Quiet)), Is.EqualTo(TileStatus.AwaitingInput));
    }

    /// <summary>
    /// Пока пользователь набирает запрос, экран агента меняется каждым нажатием,
    /// но модель не работает — мигать лампочке нечем. Занятость определяется
    /// только маркером, поэтому меняющийся экран без маркера статус не двигает.
    /// </summary>
    [Test]
    public void ChangingScreenWithoutBusyMarker_NeverYieldsWorking()
    {
        var detector = Create();
        var typed = string.Empty;

        foreach (var symbol in "напиши тест")
        {
            typed += symbol;
            Advance(Tick);

            Assert.That(
                detector.Update(Snapshot([$"❯ {typed}", "", "  ⏵⏵ auto mode on (shift+tab to cycle)"])),
                Is.EqualTo(TileStatus.AwaitingInput),
                "набор текста — не работа модели");
        }
    }

    /// <summary>
    /// Паттерн подтверждения, увиденный один тик, статус не переключает.
    /// </summary>
    [Test]
    public void PermissionPattern_SingleTick_DoesNotFlipStatus()
    {
        var detector = Create();
        Advance(Tick);

        var status = detector.Update(Snapshot(PermissionRows));

        Assert.That(status, Is.Not.EqualTo(TileStatus.AwaitingPermission), "одного тика недостаточно");
    }

    /// <summary>
    /// Паттерн, устоявшийся дольше выдержки, переключает в AwaitingPermission.
    /// </summary>
    [Test]
    public void PermissionPattern_StableBeyondPersistence_YieldsAwaitingPermission()
    {
        var detector = Create();
        var status = TileStatus.Running;

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            status = detector.Update(Snapshot(PermissionRows));
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
            detector.Update(Snapshot(i % 2 == 0 ? PermissionRows : Quiet));
        }

        Assert.That(detector.Status, Is.Not.EqualTo(TileStatus.AwaitingPermission), "мерцание не должно подтверждаться");
    }

    /// <summary>
    /// Пользователь ответил на запрос, агент вернулся к работе — статус идёт за
    /// маркером занятости, не дожидаясь выдержки.
    /// </summary>
    [Test]
    public void PermissionAnswered_BusyMarkerReturns_YieldsWorking()
    {
        var detector = Create();

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(PermissionRows));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.AwaitingPermission));

        Advance(Tick);

        Assert.That(detector.Update(Snapshot(BusyRows)), Is.EqualTo(TileStatus.Working));
    }

    /// <summary>
    /// Диалог закрылся, работать агент не стал — ход за пользователем.
    /// </summary>
    [Test]
    public void PermissionPattern_Disappears_YieldsAwaitingInput()
    {
        var detector = Create();

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(PermissionRows));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.AwaitingPermission));

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(Quiet));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.AwaitingInput));
    }

    /// <summary>
    /// Подтверждение приоритетнее занятости и на уровне детектора: диалог висит
    /// поверх работающего индикатора, но ждут-то пользователя.
    /// </summary>
    [Test]
    public void PermissionOverBusy_YieldsAwaitingPermission()
    {
        var detector = Create();
        string[] both = ["✻ Scurrying… (9s)", "│ Do you want to proceed?"];

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot(both));
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
            detector.Update(Snapshot(PermissionRows));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.AwaitingPermission));

        Advance(Tick);

        Assert.That(
            detector.Update(new StatusSnapshot(PermissionRows, true, 1)),
            Is.EqualTo(TileStatus.Crashed));
    }

    /// <summary>
    /// В обычном терминале лампочка просто горит: состояний у shell с экрана не
    /// прочитать — он ждёт ввода и когда молчит, и когда гоняет сборку. Замерший
    /// буфер такой тайл в ожидание ввода не уводит.
    /// </summary>
    [Test]
    public void Terminal_QuietBuffer_StaysRunning()
    {
        var detector = Create(AgentKind.Script);

        for (var i = 0; i < 20; i++)
        {
            Advance(Tick);
            Assert.That(detector.Update(Snapshot(Quiet)), Is.EqualTo(TileStatus.Running));
        }
    }

    /// <summary>
    /// Терминал не мигает и не просит подтверждений, даже если на экране текст,
    /// похожий на диалог агента: в нём это просто чей-то вывод.
    /// </summary>
    [TestCase("Do you want to proceed?")]
    [TestCase("✻ Scurrying… (3s)")]
    [TestCase("Working (12s • Esc to interrupt)")]
    public void Terminal_AgentLookalikeOutput_StaysRunning(string line)
    {
        var detector = Create(AgentKind.Script);

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            detector.Update(Snapshot([line]));
        }

        Assert.That(detector.Status, Is.EqualTo(TileStatus.Running));
    }

    /// <summary>
    /// Завершение процесса терминал всё же меняет — код возврата выше типа утилиты.
    /// </summary>
    [Test]
    public void Terminal_Exit_ReportsOutcome()
    {
        var detector = Create(AgentKind.Script);

        Assert.That(detector.Update(new StatusSnapshot(Quiet, true, 2)), Is.EqualTo(TileStatus.Crashed));
    }

    /// <summary>
    /// Свежесозданный детектор считает процесс просто живым: лампочка загорается
    /// сразу после запуска, а состояние агента появится на первом снимке.
    /// </summary>
    [Test]
    public void NewDetector_StartsRunning()
    {
        Assert.That(Create().Status, Is.EqualTo(TileStatus.Running));
    }

    /// <summary>
    /// Пока агент не нарисовал ни строки, статус не двигается: пустой экран —
    /// это не отработанный запрос. Первый опрос приходит через полсекунды после
    /// запуска, когда CLI ещё поднимается, и объявлять «ход за пользователем»
    /// там не за что.
    /// </summary>
    [Test]
    public void EmptyScreen_StaysRunning_UntilAgentDrawsSomething()
    {
        var detector = Create();

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            Assert.That(detector.Update(Snapshot([])), Is.EqualTo(TileStatus.Running), "рисовать ещё нечего");
        }

        Advance(Tick);

        Assert.That(detector.Update(Snapshot(Quiet)), Is.EqualTo(TileStatus.AwaitingInput), "экран появился — ход за пользователем");
    }

    /// <summary>
    /// Экран, опустевший на кадр перерисовки, занятость не снимает: маркера на
    /// нём нет, но и признать запрос отработанным по пустоте нельзя.
    /// </summary>
    [Test]
    public void EmptyScreen_AfterBusyMarker_HoldsWorking()
    {
        var detector = Create();

        Advance(Tick);

        Assert.That(detector.Update(Snapshot(BusyRows)), Is.EqualTo(TileStatus.Working));

        for (var i = 0; i < 4; i++)
        {
            Advance(Tick);
            Assert.That(detector.Update(Snapshot([])), Is.EqualTo(TileStatus.Working), "пустой экран статус не меняет");
        }
    }

    /// <summary>
    /// Все известные агенты умеют мигать: маркер занятости для каждого из них
    /// даёт Working. Тип без паттернов живёт по правилам терминала.
    /// </summary>
    [TestCase(AgentKind.Claude, "✻ Scurrying… (4s)")]
    [TestCase(AgentKind.Codex, "Working (12s • Esc to interrupt)")]
    [TestCase(AgentKind.CursorAgent, "  ⠰⠳ Working")]
    [TestCase(AgentKind.OpenCode, "thinking… (esc to interrupt)")]
    public void KnownAgents_BusyMarker_YieldsWorking(AgentKind kind, string line)
    {
        var detector = Create(kind);
        Advance(Tick);

        Assert.That(detector.Update(Snapshot([line])), Is.EqualTo(TileStatus.Working));
    }

    private AgentStatusDetector Create(AgentKind kind = AgentKind.Claude)
        => new(kind, () => _now, Persistence);

    private static StatusSnapshot Snapshot(IReadOnlyList<string> rows) => new(rows, false, null);

    private void Advance(TimeSpan span) => _now += span;
}
