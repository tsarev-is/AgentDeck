using AgentDeck.Settings;
using AgentDeck.ViewModels;
using NUnit.Framework;

namespace AgentDeck.Tests.Settings;

/// <summary>
/// Модель окна настроек: добавление и удаление строк, сборка настроек обратно.
/// </summary>
[TestFixture]
public class SettingsViewModelTests
{
    /// <summary>
    /// Окно открывается на копии: правки до «Save» не трогают текущие настройки.
    /// </summary>
    [Test]
    public void Edits_DoNotTouchSourceSettings()
    {
        var settings = AppSettings.CreateDefault();
        var model = new SettingsViewModel(settings);

        model.DefaultDirectory = "/changed";
        model.Utilities[0].Command = "/changed/claude";
        model.Utilities[0].IsEnabled = false;

        Assert.Multiple(() =>
        {
            Assert.That(settings.DefaultDirectory, Is.Not.EqualTo("/changed"));
            Assert.That(settings.Utilities[0].Command, Is.EqualTo("claude"));
            Assert.That(settings.Utilities[0].Enabled, Is.True);
        });
    }

    /// <summary>
    /// «+ Add» добавляет включённую строку в конец таблицы.
    /// </summary>
    [Test]
    public void AddUtility_AppendsEnabledRow()
    {
        var model = new SettingsViewModel(AppSettings.CreateDefault());
        var count = model.Utilities.Count;

        var added = model.AddUtility();

        Assert.Multiple(() =>
        {
            Assert.That(model.Utilities, Has.Count.EqualTo(count + 1));
            Assert.That(model.Utilities[^1], Is.SameAs(added));
            Assert.That(added.IsEnabled, Is.True);
            Assert.That(added.Command, Is.Empty);
        });
    }

    /// <summary>
    /// ✕ на строке убирает её из таблицы.
    /// </summary>
    [Test]
    public void RemoveUtility_DropsRow()
    {
        var model = new SettingsViewModel(AppSettings.CreateDefault());
        var victim = model.Utilities.Single(row => row.Name == "opencode");

        model.RemoveUtility(victim);

        Assert.That(model.Utilities.Any(row => row.Name == "opencode"), Is.False);
    }

    /// <summary>
    /// Сохранение обрезает пробелы и выбрасывает пустые строки.
    /// </summary>
    [Test]
    public void ToSettings_TrimsValuesAndDropsBlankRows()
    {
        var model = new SettingsViewModel(new AppSettings
        {
            DefaultDirectory = "  /home/user/dev  ",
            Utilities = [new UtilityState { Name = "  codex  ", Command = "  ~/bin/codex  ", Enabled = true }],
        });

        model.AddUtility().Name = "   ";

        var saved = model.ToSettings();

        Assert.Multiple(() =>
        {
            Assert.That(saved.SettingsVersion, Is.EqualTo(AppSettings.CurrentVersion));
            Assert.That(saved.DefaultDirectory, Is.EqualTo("/home/user/dev"));
            Assert.That(saved.Utilities, Has.Count.EqualTo(1));
            Assert.That(saved.Utilities[0].Name, Is.EqualTo("codex"));
            Assert.That(saved.Utilities[0].Command, Is.EqualTo("~/bin/codex"));
        });
    }

    /// <summary>
    /// Выключенная строка гасится, как в макете.
    /// </summary>
    [Test]
    public void RowOpacity_FollowsEnabledFlag()
    {
        var row = new UtilityRowViewModel(new UtilityState { Name = "codex", Enabled = true });

        Assert.That(row.RowOpacity, Is.EqualTo(1.0));

        row.IsEnabled = false;

        Assert.That(row.RowOpacity, Is.EqualTo(0.45).Within(1e-9));
    }

    /// <summary>
    /// Строка без идентификатора получает свой при загрузке — сохранение
    /// не должно ронять привязку к утилите.
    /// </summary>
    [Test]
    public void Row_WithoutId_GetsGeneratedOne()
    {
        var row = new UtilityRowViewModel(new UtilityState { Name = "codex" });

        Assert.That(row.Id, Is.Not.Empty);
        Assert.That(row.ToState().Id, Is.EqualTo(row.Id));
    }
}
