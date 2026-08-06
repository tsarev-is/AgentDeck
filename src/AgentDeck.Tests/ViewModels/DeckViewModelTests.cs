using AgentDeck.Layout;
using AgentDeck.Models;
using AgentDeck.Settings;
using AgentDeck.ViewModels;
using NUnit.Framework;

namespace AgentDeck.Tests.ViewModels;

/// <summary>
/// Дек: кап тайлов, закрытие и вывод имени проекта из пути.
/// </summary>
[TestFixture]
public class DeckViewModelTests
{
    /// <summary>
    /// Дек добавляет ровно восемь тайлов, девятый — no-op.
    /// </summary>
    [Test]
    public void AddTile_StopsAtEightTiles()
    {
        var deck = new DeckViewModel();

        for (var i = 0; i < LayoutConstants.MaxTiles; i++)
        {
            Assert.That(deck.AddTile(), Is.Not.Null, $"тайл #{i + 1} должен добавиться");
        }

        Assert.Multiple(() =>
        {
            Assert.That(deck.CanAddTile, Is.False, "на восьми тайлах кнопка должна быть заблокирована");
            Assert.That(deck.AddTile(), Is.Null, "девятый тайл не должен добавляться");
            Assert.That(deck.Tiles, Has.Count.EqualTo(LayoutConstants.MaxTiles));
            Assert.That(deck.Layout.LeafCount, Is.EqualTo(LayoutConstants.MaxTiles));
        });
    }

    /// <summary>
    /// Пустой дек сообщает об этом и разрешает добавление.
    /// </summary>
    [Test]
    public void NewDeck_IsEmptyAndAllowsAdding()
    {
        var deck = new DeckViewModel();

        Assert.Multiple(() =>
        {
            Assert.That(deck.IsEmpty, Is.True);
            Assert.That(deck.CanAddTile, Is.True);
            Assert.That(deck.Layout.Root, Is.Null);
        });
    }

    /// <summary>
    /// Закрытие тайла удаляет и сам тайл, и лист раскладки.
    /// </summary>
    [Test]
    public void CloseTile_RemovesTileAndLayoutLeaf()
    {
        var deck = new DeckViewModel();
        var first = deck.AddTile()!;
        var second = deck.AddTile()!;

        Assert.That(deck.CloseTile(first.Id), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(deck.Tiles, Has.Count.EqualTo(1));
            Assert.That(deck.Tiles[0].Id, Is.EqualTo(second.Id));
            Assert.That(deck.Layout.Contains(first.Id), Is.False);
            Assert.That(deck.Layout.LeafCount, Is.EqualTo(1));
            Assert.That(deck.Layout.RectOf(second.Id, RectD.Unit), Is.EqualTo(RectD.Unit), "сосед поглощает место");
        });
    }

    /// <summary>
    /// После закрытия на полном деке снова можно добавлять.
    /// </summary>
    [Test]
    public void CloseTile_ReenablesAdding()
    {
        var deck = new DeckViewModel();
        var tiles = Enumerable.Range(0, LayoutConstants.MaxTiles).Select(_ => deck.AddTile()!).ToList();

        Assert.That(deck.CanAddTile, Is.False);

        deck.CloseTile(tiles[3].Id);

        Assert.Multiple(() =>
        {
            Assert.That(deck.CanAddTile, Is.True);
            Assert.That(deck.AddTile(), Is.Not.Null);
        });
    }

    /// <summary>
    /// Кнопка ✕ на тайле закрывает его через дек.
    /// </summary>
    [Test]
    public void TileCloseRequest_ClosesTileThroughDeck()
    {
        var deck = new DeckViewModel();
        var tile = deck.AddTile()!;
        TileViewModel? closed = null;
        deck.TileClosed += (_, t) => closed = t;

        tile.RequestClose();

        Assert.Multiple(() =>
        {
            Assert.That(deck.Tiles, Is.Empty);
            Assert.That(closed, Is.SameAs(tile));
        });
    }

    /// <summary>
    /// Запрос запуска с тайла доходит до дека вместе с выбранной утилитой.
    /// </summary>
    [Test]
    public void TileLaunchRequest_BubblesToDeck()
    {
        var deck = new DeckViewModel();
        var tile = deck.AddTile()!;
        (TileViewModel Tile, LaunchOptionViewModel Option)? received = null;
        deck.LaunchRequested += (_, e) => received = e;

        var claude = tile.LaunchOptions.Single(o => o.Name == "claude");
        tile.RequestLaunch(claude);

        Assert.That(received, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(received!.Value.Tile, Is.SameAs(tile));
            Assert.That(received.Value.Option, Is.SameAs(claude));
            Assert.That(received.Value.Option.Kind, Is.EqualTo(AgentKind.Claude));
        });
    }

    /// <summary>
    /// Закрытый тайл больше не поднимает событий в деке.
    /// </summary>
    [Test]
    public void ClosedTile_IsDetachedFromDeck()
    {
        var deck = new DeckViewModel();
        var first = deck.AddTile()!;
        deck.AddTile();
        deck.CloseTile(first.Id);

        var launches = 0;
        deck.LaunchRequested += (_, _) => launches++;
        first.RequestLaunch(first.LaunchOptions[0]);

        Assert.That(launches, Is.Zero);
    }

    /// <summary>
    /// Закрытие неизвестного тайла — no-op.
    /// </summary>
    [Test]
    public void CloseTile_UnknownId_ReturnsFalse()
    {
        var deck = new DeckViewModel();
        deck.AddTile();

        Assert.That(deck.CloseTile(Guid.NewGuid()), Is.False);
        Assert.That(deck.Tiles, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Раскладка после серии добавлений остаётся валидной и без дыр.
    /// </summary>
    [Test]
    public void AddTile_KeepsLayoutValidAtEveryStep()
    {
        var deck = new DeckViewModel();

        for (var i = 0; i < LayoutConstants.MaxTiles; i++)
        {
            deck.AddTile();

            Assert.That(deck.Layout.Validate(), Is.True, $"инварианты на {i + 1} тайлах");
            Assert.That(deck.Layout.Project().Sum(t => t.Rect.Area), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(deck.Layout.MinSizeSatisfied(), Is.True);
        }
    }

    /// <summary>
    /// Новый тайл получает директорию по умолчанию.
    /// </summary>
    [Test]
    public void AddTile_UsesDefaultDirectory()
    {
        var deck = new DeckViewModel { DefaultDirectory = "/tmp/work" };

        Assert.That(deck.AddTile()!.Directory, Is.EqualTo("/tmp/work"));
    }

    /// <summary>
    /// Имя проекта — последний сегмент пути; хвостовой разделитель игнорируется,
    /// корень показывается как есть, разделители обеих платформ понимаются.
    /// </summary>
    [TestCase("/home/user/dev/api-gateway", "api-gateway")]
    [TestCase("/home/user/dev/api-gateway/", "api-gateway")]
    [TestCase("/home/user/dev/api-gateway///", "api-gateway")]
    [TestCase("/", "/")]
    [TestCase("//", "//")]
    [TestCase(@"C:\x\y", "y")]
    [TestCase(@"C:\x\y\", "y")]
    [TestCase(@"C:\", "C:")]
    [TestCase("~/dev/core", "core")]
    [TestCase("relative/path", "path")]
    [TestCase("single", "single")]
    [TestCase("  /home/user/dev/api  ", "api")]
    [TestCase("", TileViewModel.UntitledName)]
    [TestCase("   ", TileViewModel.UntitledName)]
    [TestCase(null, TileViewModel.UntitledName)]
    public void DeriveTitle_ReturnsLastPathSegment(string? directory, string expected)
    {
        Assert.That(TileViewModel.DeriveTitle(directory), Is.EqualTo(expected));
    }

    /// <summary>
    /// Смена директории пересчитывает заголовок тайла.
    /// </summary>
    [Test]
    public void Directory_Change_UpdatesTitle()
    {
        var tile = new TileViewModel(Guid.NewGuid(), "/home/user");
        var notified = new List<string?>();
        tile.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        tile.Directory = "/home/user/dev/billing";

        Assert.Multiple(() =>
        {
            Assert.That(tile.Title, Is.EqualTo("billing"));
            Assert.That(notified, Does.Contain(nameof(TileViewModel.Title)));
        });
    }

    /// <summary>
    /// Подсказка сохранённой утилиты акцентирует ровно одну кнопку запуска.
    /// </summary>
    [Test]
    public void SuggestAgent_HighlightsExactlyOneOption()
    {
        var tile = new TileViewModel(Guid.NewGuid(), "/tmp");
        tile.SetLaunchOptions(AppSettings.CreateDefault().EnabledUtilities());

        tile.SuggestAgent(nameof(AgentKind.Codex), "codex");

        Assert.Multiple(() =>
        {
            Assert.That(tile.UtilityId, Is.EqualTo(nameof(AgentKind.Codex)));
            Assert.That(tile.UtilityName, Is.EqualTo("codex"));
            Assert.That(tile.LaunchOptions.Count(o => o.IsSuggested), Is.EqualTo(1));
            Assert.That(tile.LaunchOptions.Single(o => o.IsSuggested).Kind, Is.EqualTo(AgentKind.Codex));
        });

        tile.SuggestAgent(null, null);
        Assert.That(tile.LaunchOptions.Any(o => o.IsSuggested), Is.False);
    }

    /// <summary>
    /// Плейсхолдер виден только до запуска, кнопка ↻ — только после падения или завершения.
    /// </summary>
    [Test]
    public void Status_DrivesPlaceholderAndRestartVisibility()
    {
        var tile = new TileViewModel(Guid.NewGuid(), "/tmp");

        Assert.Multiple(() =>
        {
            Assert.That(tile.IsPlaceholder, Is.True);
            Assert.That(tile.CanRestart, Is.False);
        });

        tile.Status = AgentDeck.Status.TileStatus.Running;
        Assert.Multiple(() =>
        {
            Assert.That(tile.IsPlaceholder, Is.False);
            Assert.That(tile.CanRestart, Is.False);
        });

        tile.Status = AgentDeck.Status.TileStatus.Crashed;
        Assert.That(tile.CanRestart, Is.True);

        tile.Status = AgentDeck.Status.TileStatus.Finished;
        Assert.That(tile.CanRestart, Is.True);
    }

    /// <summary>
    /// Лампочку в заголовке разметка собирает из признаков статуса, поэтому на
    /// каждый статус поднят ровно один признак: два разом дали бы точке два
    /// стиля, и «горит» смешалось бы с «мигает».
    /// </summary>
    [Test]
    public void Status_RaisesExactlyOneFlag()
    {
        var tile = new TileViewModel(Guid.NewGuid(), "/tmp");

        var flags = new Dictionary<AgentDeck.Status.TileStatus, Func<bool>>
        {
            [AgentDeck.Status.TileStatus.Placeholder] = () => tile.IsPlaceholder,
            [AgentDeck.Status.TileStatus.Running] = () => tile.IsRunning,
            [AgentDeck.Status.TileStatus.Working] = () => tile.IsWorking,
            [AgentDeck.Status.TileStatus.AwaitingInput] = () => tile.IsAwaitingInput,
            [AgentDeck.Status.TileStatus.AwaitingPermission] = () => tile.IsAwaitingPermission,
            [AgentDeck.Status.TileStatus.Finished] = () => tile.IsFinished,
            [AgentDeck.Status.TileStatus.Crashed] = () => tile.IsCrashed,
        };

        foreach (var status in Enum.GetValues<AgentDeck.Status.TileStatus>())
        {
            tile.Status = status;

            Assert.Multiple(() =>
            {
                Assert.That(flags[status](), Is.True, $"признак статуса {status} должен быть поднят");
                Assert.That(flags.Count(flag => flag.Value()), Is.EqualTo(1), $"у статуса {status} признак один");
            });
        }
    }

    /// <summary>
    /// Смена статуса рассылает уведомления по всем признакам: классы точки
    /// статуса привязаны именно к ним, и без уведомления лампочка застыла бы на
    /// прежнем виде.
    /// </summary>
    [Test]
    public void Status_Change_NotifiesEveryFlag()
    {
        var tile = new TileViewModel(Guid.NewGuid(), "/tmp");
        var notified = new List<string?>();
        tile.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        tile.Status = AgentDeck.Status.TileStatus.Working;

        Assert.That(
            notified,
            Is.SupersetOf(new[]
            {
                nameof(TileViewModel.IsPlaceholder),
                nameof(TileViewModel.IsRunning),
                nameof(TileViewModel.IsWorking),
                nameof(TileViewModel.IsAwaitingInput),
                nameof(TileViewModel.IsAwaitingPermission),
                nameof(TileViewModel.IsFinished),
                nameof(TileViewModel.IsCrashed),
            }));
    }

    /// <summary>
    /// Штатный выход из агента закрывает тайл: мёртвый терминал не принимает
    /// ввод и в раскладке остаётся зависшим прямоугольником.
    /// </summary>
    [Test]
    public void ProcessExit_WithZeroCode_ClosesTile()
    {
        var deck = new DeckViewModel();
        var tile = deck.AddTile();

        Assert.That(tile, Is.Not.Null);

        tile.NotifyProcessExited(0);

        Assert.Multiple(() =>
        {
            Assert.That(deck.Tiles, Does.Not.Contain(tile));
            Assert.That(deck.Layout.LeafCount, Is.Zero);
            Assert.That(deck.IsEmpty, Is.True);
        });
    }

    /// <summary>
    /// Упавший процесс тайл сохраняет: на экране остаются его вывод и кнопка ↻.
    /// </summary>
    [Test]
    public void ProcessExit_WithNonZeroCode_KeepsTileForRestart()
    {
        var deck = new DeckViewModel();
        var tile = deck.AddTile();

        Assert.That(tile, Is.Not.Null);

        tile.NotifyProcessExited(1);

        Assert.Multiple(() =>
        {
            Assert.That(deck.Tiles, Does.Contain(tile));
            Assert.That(tile.Status, Is.EqualTo(AgentDeck.Status.TileStatus.Crashed));
            Assert.That(tile.CanRestart, Is.True);
        });
    }

    /// <summary>
    /// Каждое изменение состава тайлов поднимает LayoutChanged для канвы и персиста.
    /// </summary>
    [Test]
    public void StructuralChanges_RaiseLayoutChanged()
    {
        var deck = new DeckViewModel();
        var raised = 0;
        deck.LayoutChanged += (_, _) => raised++;

        var tile = deck.AddTile()!;
        Assert.That(raised, Is.EqualTo(1));

        deck.CloseTile(tile.Id);
        Assert.That(raised, Is.EqualTo(2));

        deck.NotifyLayoutChanged();
        Assert.That(raised, Is.EqualTo(3));
    }

    /// <summary>
    /// Кнопки запуска нового тайла — это включённые утилиты из настроек,
    /// в том же порядке и с той же командой.
    /// </summary>
    [Test]
    public void AddTile_LaunchOptionsFollowEnabledUtilities()
    {
        var deck = new DeckViewModel(new AppSettings
        {
            Utilities =
            [
                new UtilityState { Name = "codex", Command = "~/.local/bin/codex", Enabled = true },
                new UtilityState { Name = "opencode", Command = "opencode", Enabled = false },
                new UtilityState { Name = "shell", Command = string.Empty, Enabled = true },
            ],
        });

        var tile = deck.AddTile()!;

        Assert.Multiple(() =>
        {
            Assert.That(tile.LaunchOptions.Select(o => o.Name), Is.EqualTo(new[] { "codex", "shell" }));
            Assert.That(tile.LaunchOptions[0].Command, Is.EqualTo("~/.local/bin/codex"));
            Assert.That(tile.LaunchOptions[0].Kind, Is.EqualTo(AgentKind.Codex));
            Assert.That(tile.LaunchOptions[1].Command, Is.Empty);
        });
    }

    /// <summary>
    /// Сохранение настроек пересобирает кнопки у уже открытых тайлов и меняет
    /// директорию для следующих.
    /// </summary>
    [Test]
    public void ApplyUtilities_RefreshesExistingTilesAndDefaultDirectory()
    {
        var deck = new DeckViewModel();
        var existing = deck.AddTile()!;
        existing.SuggestAgent(null, "codex");

        // Путь внутри домашней папки: тайл обязан взять его в короткой форме.
        var configured = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "dev");

        deck.ApplyUtilities(new AppSettings
        {
            DefaultDirectory = configured,
            Utilities =
            [
                new UtilityState { Name = "codex", Command = "/opt/codex", Enabled = true },
                new UtilityState { Name = "htop", Command = "htop", Enabled = true },
            ],
        });

        Assert.Multiple(() =>
        {
            Assert.That(existing.LaunchOptions.Select(o => o.Name), Is.EqualTo(new[] { "codex", "htop" }));
            Assert.That(existing.LaunchOptions[0].Command, Is.EqualTo("/opt/codex"), "команда должна обновиться");
            Assert.That(existing.LaunchOptions.Single(o => o.IsSuggested).Name, Is.EqualTo("codex"), "подсказка переживает пересборку");
            Assert.That(
                deck.AddTile()!.Directory,
                Is.EqualTo($"~{Path.DirectorySeparatorChar}dev"),
                "тайл живёт с коротким путём");
        });
    }

    /// <summary>
    /// Очищенная директория по умолчанию означает домашнюю папку, а не
    /// «оставить прежнюю»: иначе настройка не применилась бы до перезапуска.
    /// </summary>
    [Test]
    public void ApplyUtilities_BlankDefaultDirectory_FallsBackToHome()
    {
        var deck = new DeckViewModel();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var settings = AppSettings.CreateDefault();
        settings.DefaultDirectory = "/home/user/dev";
        deck.ApplyUtilities(settings);

        Assert.That(deck.DefaultDirectory, Is.EqualTo("/home/user/dev"));

        settings.DefaultDirectory = "   ";
        deck.ApplyUtilities(settings);

        Assert.Multiple(() =>
        {
            Assert.That(deck.DefaultDirectory, Is.EqualTo(home));
            Assert.That(deck.AddTile()!.Directory, Is.EqualTo("~"), "новый тайл встаёт в домашнюю папку");
        });
    }

    /// <summary>
    /// Утилита, выключенная в настройках, исчезает с плейсхолдера.
    /// </summary>
    [Test]
    public void ApplyUtilities_DisabledUtility_DisappearsFromTile()
    {
        var deck = new DeckViewModel();
        var tile = deck.AddTile()!;

        Assert.That(tile.LaunchOptions.Any(o => o.Name == "opencode"), Is.True);

        var settings = AppSettings.CreateDefault();
        settings.Utilities.Single(u => u.Name == "opencode").Enabled = false;
        deck.ApplyUtilities(settings);

        Assert.That(tile.LaunchOptions.Any(o => o.Name == "opencode"), Is.False);
    }

    /// <summary>
    /// Сессия старого формата хранила элемент перечисления — подсказка всё равно
    /// должна найти свою кнопку.
    /// </summary>
    [Test]
    public void RestoreSession_LegacyAgentKind_HighlightsUtility()
    {
        var source = new DeckViewModel();
        var tile = source.AddTile("/tmp", null)!;

        var state = source.CaptureSession();
        state.Tiles[0].Utility = null;
        state.Tiles[0].AgentKind = nameof(AgentKind.CursorAgent);

        var restored = new DeckViewModel();
        Assert.That(restored.RestoreSession(state), Is.True);

        var suggested = restored.FindTile(tile.Id)!.LaunchOptions.Single(o => o.IsSuggested);
        Assert.That(suggested.Name, Is.EqualTo("cursor-agent"));
    }

    /// <summary>
    /// Ненайденная команда не поднимает PTY: тайл остаётся плейсхолдером
    /// с внятным сообщением вместо «command not found» внутри терминала.
    /// </summary>
    [Test]
    public async Task LaunchAsync_MissingCommand_KeepsPlaceholderAndPointsToSettings()
    {
        var tile = new TileViewModel(Guid.NewGuid(), Path.GetTempPath(), new CommandResolver(_ => false));
        tile.SetLaunchOptions([new UtilityState { Name = "codex", Command = "codex-not-installed", Enabled = true }]);

        await tile.LaunchAsync(tile.LaunchOptions[0]);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Status, Is.EqualTo(AgentDeck.Status.TileStatus.Placeholder));
            Assert.That(tile.Terminal, Is.Null, "процесс не должен подниматься");
            Assert.That(tile.Error, Does.Contain("codex-not-installed").And.Contains("Settings"));
        });
    }

    /// <summary>
    /// Несуществующая директория тоже останавливает запуск до проверки команды.
    /// </summary>
    [Test]
    public async Task LaunchAsync_MissingDirectory_ReportsErrorBeforeCommandCheck()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"agentdeck-missing-{Guid.NewGuid():N}");
        var tile = new TileViewModel(
            Guid.NewGuid(),
            directory,
            new CommandResolver(_ => throw new AssertionException("команда не должна проверяться")));

        tile.SetLaunchOptions([new UtilityState { Name = "codex", Command = "codex", Enabled = true }]);

        await tile.LaunchAsync(tile.LaunchOptions[0]);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Status, Is.EqualTo(AgentDeck.Status.TileStatus.Placeholder));
            Assert.That(tile.Error, Does.StartWith("Directory not found:").And.Contains(directory));
        });
    }
}
