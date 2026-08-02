using AgentDeck.Settings;
using NUnit.Framework;

namespace AgentDeck.Tests.Settings;

/// <summary>
/// Хранилище настроек: round-trip, дефолты вместо повреждённых данных,
/// атомарность записи.
/// </summary>
[TestFixture]
public class SettingsStoreTests
{
    private string _root = null!;
    private SettingsStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"agentdeck-settings-{Guid.NewGuid():N}");
        _store = new SettingsStore(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Сохранённые настройки читаются без потерь.
    /// </summary>
    [Test]
    public void RoundTrip_PreservesDirectoryAndUtilities()
    {
        var settings = BuildSettings();

        Assert.That(_store.Save(settings), Is.True);

        var loaded = _store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.SettingsVersion, Is.EqualTo(AppSettings.CurrentVersion));
            Assert.That(loaded.DefaultDirectory, Is.EqualTo("/home/user/dev"));
            Assert.That(loaded.Utilities, Has.Count.EqualTo(2));
            Assert.That(loaded.Utilities[0].Name, Is.EqualTo("codex"));
            Assert.That(loaded.Utilities[0].Command, Is.EqualTo("~/.local/bin/codex"));
            Assert.That(loaded.Utilities[0].Enabled, Is.True);
            Assert.That(loaded.Utilities[1].Name, Is.EqualTo("opencode"));
            Assert.That(loaded.Utilities[1].Enabled, Is.False);
        });
    }

    /// <summary>
    /// Файл кладётся в подкаталог приложения.
    /// </summary>
    [Test]
    public void Save_WritesIntoApplicationFolder()
    {
        _store.Save(BuildSettings());

        Assert.Multiple(() =>
        {
            Assert.That(_store.Directory, Is.EqualTo(Path.Combine(_root, SettingsStore.FolderName)));
            Assert.That(_store.FilePath, Is.EqualTo(Path.Combine(_store.Directory, SettingsStore.FileName)));
            Assert.That(File.Exists(_store.FilePath), Is.True);
        });
    }

    /// <summary>
    /// Первого запуска ещё не было — отдаём штатный набор утилит.
    /// </summary>
    [Test]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var loaded = _store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Utilities.Select(u => u.Name), Is.EqualTo(new[] { "claude", "codex", "opencode", "cursor-agent", "script" }));
            Assert.That(loaded.Utilities.All(u => u.Enabled), Is.True);
            Assert.That(loaded.DefaultDirectory, Is.Not.Empty);
        });
    }

    /// <summary>
    /// «script» по умолчанию — обычный shell, то есть пустая команда.
    /// </summary>
    [Test]
    public void Defaults_ScriptHasEmptyCommand()
    {
        var script = AppSettings.CreateDefault().Utilities.Single(u => u.Name == "script");

        Assert.That(script.Command, Is.Empty);
    }

    /// <summary>
    /// Битый JSON не должен ронять запуск — возвращаются дефолты.
    /// </summary>
    [Test]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_store.Directory);
        File.WriteAllText(_store.FilePath, "{ это не json ");

        Assert.That(_store.Load().Utilities, Is.Not.Empty);
    }

    /// <summary>
    /// Чужая версия формата отбрасывается целиком.
    /// </summary>
    [Test]
    public void Load_ForeignVersion_ReturnsDefaults()
    {
        _store.Save(new AppSettings
        {
            SettingsVersion = AppSettings.CurrentVersion + 1,
            DefaultDirectory = "/ghost",
            Utilities = [new UtilityState { Name = "ghost", Command = "ghost" }],
        });

        Assert.That(_store.Load().DefaultDirectory, Is.Not.EqualTo("/ghost"));
    }

    /// <summary>
    /// «utilities»: null не должен ронять загрузку.
    /// </summary>
    [Test]
    public void Load_NullUtilities_ReturnsEmptyList()
    {
        Directory.CreateDirectory(_store.Directory);
        File.WriteAllText(_store.FilePath, """{ "settingsVersion": 1, "defaultDirectory": "/tmp", "utilities": null }""");

        Assert.That(_store.Load().Utilities, Is.Empty);
    }

    /// <summary>
    /// Запись не оставляет временного файла.
    /// </summary>
    [Test]
    public void Save_LeavesNoTemporaryFile()
    {
        _store.Save(BuildSettings());

        Assert.That(File.Exists(_store.FilePath + ".tmp"), Is.False);
    }

    /// <summary>
    /// Повторная запись перезаписывает файл целиком.
    /// </summary>
    [Test]
    public void Save_Twice_OverwritesPreviousContent()
    {
        _store.Save(BuildSettings());
        _store.Save(new AppSettings
        {
            DefaultDirectory = "/second",
            Utilities = [new UtilityState { Id = "u1", Name = "htop", Command = "htop" }],
        });

        var loaded = _store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.DefaultDirectory, Is.EqualTo("/second"));
            Assert.That(loaded.Utilities, Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// Копия настроек независима от оригинала — «Отмена» ничего не портит.
    /// </summary>
    [Test]
    public void Clone_DoesNotShareUtilityInstances()
    {
        var settings = BuildSettings();
        var copy = settings.Clone();

        copy.Utilities[0].Command = "changed";
        copy.DefaultDirectory = "/changed";

        Assert.Multiple(() =>
        {
            Assert.That(settings.Utilities[0].Command, Is.EqualTo("~/.local/bin/codex"));
            Assert.That(settings.DefaultDirectory, Is.EqualTo("/home/user/dev"));
        });
    }

    /// <summary>
    /// На плейсхолдере показываются только включённые и именованные утилиты.
    /// </summary>
    [Test]
    public void EnabledUtilities_SkipsDisabledAndUnnamed()
    {
        var settings = new AppSettings
        {
            Utilities =
            [
                new UtilityState { Name = "claude", Command = "claude", Enabled = true },
                new UtilityState { Name = "codex", Command = "codex", Enabled = false },
                new UtilityState { Name = "   ", Command = "ghost", Enabled = true },
            ],
        };

        Assert.That(settings.EnabledUtilities().Select(u => u.Name), Is.EqualTo(new[] { "claude" }));
    }

    private static AppSettings BuildSettings() => new()
    {
        SettingsVersion = AppSettings.CurrentVersion,
        DefaultDirectory = "/home/user/dev",
        Utilities =
        [
            new UtilityState { Id = "u1", Name = "codex", Command = "~/.local/bin/codex", Enabled = true },
            new UtilityState { Id = "u2", Name = "opencode", Command = "opencode", Enabled = false },
        ],
    };
}
