using AgentDeck.Terminal;
using Avalonia.Input;
using NUnit.Framework;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Разбор сочетаний буфера обмена: что перехватывается у процесса, а что нет.
/// </summary>
[TestFixture]
public class ClipboardGestureTests
{
    /// <summary>
    /// Ctrl+Shift+C и Ctrl+Insert копируют всегда — сочетание занято буфером
    /// обмена и до процесса дойти не должно ни при каком выделении.
    /// </summary>
    [TestCase(Key.C, KeyModifiers.Control | KeyModifiers.Shift)]
    [TestCase(Key.Insert, KeyModifiers.Control)]
    public void Resolve_CopyGesture_CopiesWithoutSelection(Key key, KeyModifiers modifiers)
        => Assert.That(
            ClipboardGesture.Resolve(key, modifiers, hasSelection: false),
            Is.EqualTo(ClipboardAction.Copy));

    /// <summary>
    /// Ctrl+C копирует только живое выделение.
    /// </summary>
    [Test]
    public void Resolve_ControlC_WithSelection_Copies()
        => Assert.That(
            ClipboardGesture.Resolve(Key.C, KeyModifiers.Control, hasSelection: true),
            Is.EqualTo(ClipboardAction.Copy));

    /// <summary>
    /// Без выделения Ctrl+C остаётся прерыванием процесса: отобрать у пользователя
    /// единственный способ остановить разогнавшегося агента нельзя.
    /// </summary>
    [Test]
    public void Resolve_ControlC_WithoutSelection_LeavesInterrupt()
        => Assert.That(
            ClipboardGesture.Resolve(Key.C, KeyModifiers.Control, hasSelection: false),
            Is.EqualTo(ClipboardAction.None));

    /// <summary>
    /// Вставку понимают все привычные сочетания.
    /// </summary>
    [TestCase(Key.V, KeyModifiers.Control)]
    [TestCase(Key.V, KeyModifiers.Control | KeyModifiers.Shift)]
    [TestCase(Key.Insert, KeyModifiers.Shift)]
    public void Resolve_PasteGesture_Pastes(Key key, KeyModifiers modifiers)
        => Assert.That(
            ClipboardGesture.Resolve(key, modifiers, hasSelection: false),
            Is.EqualTo(ClipboardAction.Paste));

    /// <summary>
    /// Alt и Meta переводят сочетание в другую роль: с ними клавиши принадлежат
    /// процессу и оконному менеджеру.
    /// </summary>
    [TestCase(Key.C, KeyModifiers.Control | KeyModifiers.Alt)]
    [TestCase(Key.V, KeyModifiers.Control | KeyModifiers.Alt)]
    [TestCase(Key.C, KeyModifiers.Control | KeyModifiers.Meta)]
    [TestCase(Key.Insert, KeyModifiers.Shift | KeyModifiers.Alt)]
    public void Resolve_WithAltOrMeta_IsNotClipboard(Key key, KeyModifiers modifiers)
        => Assert.That(
            ClipboardGesture.Resolve(key, modifiers, hasSelection: true),
            Is.EqualTo(ClipboardAction.None));

    /// <summary>
    /// Обычный набор букв и одиночный Insert уходят процессу целиком.
    /// </summary>
    [TestCase(Key.C, KeyModifiers.None)]
    [TestCase(Key.V, KeyModifiers.None)]
    [TestCase(Key.C, KeyModifiers.Shift)]
    [TestCase(Key.Insert, KeyModifiers.None)]
    [TestCase(Key.Insert, KeyModifiers.Control | KeyModifiers.Shift)]
    public void Resolve_WithoutControl_IsNotClipboard(Key key, KeyModifiers modifiers)
        => Assert.That(
            ClipboardGesture.Resolve(key, modifiers, hasSelection: true),
            Is.EqualTo(ClipboardAction.None));
}
