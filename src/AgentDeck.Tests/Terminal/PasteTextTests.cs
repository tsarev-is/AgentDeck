using AgentDeck.Terminal;
using NUnit.Framework;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Подготовка вставки: переводы строк, управляющие символы и обёртка
/// bracketed paste.
/// </summary>
[TestFixture]
public class PasteTextTests
{
    /// <summary>
    /// Пустому буферу обмена нечего отдавать процессу — обёртка тоже не нужна.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    public void Prepare_EmptyText_ReturnsEmpty(string? text)
    {
        Assert.Multiple(() =>
        {
            Assert.That(PasteText.Prepare(text, bracketed: false), Is.Empty);
            Assert.That(PasteText.Prepare(text, bracketed: true), Is.Empty);
        });
    }

    /// <summary>
    /// Любой перевод строки превращается в CR: ведомый конец PTY отдан в
    /// raw-режиме и Enter узнаёт только по нему.
    /// </summary>
    [TestCase("a\nb", "a\rb")]
    [TestCase("a\r\nb", "a\rb")]
    [TestCase("a\rb", "a\rb")]
    [TestCase("a\r\n\r\nb", "a\r\rb")]
    public void Prepare_Newlines_BecomeCarriageReturns(string text, string expected)
        => Assert.That(PasteText.Prepare(text, bracketed: false), Is.EqualTo(expected));

    /// <summary>
    /// Табуляция остаётся: в отбивке вставленного кода она значима.
    /// </summary>
    [Test]
    public void Prepare_Tab_Survives()
        => Assert.That(PasteText.Prepare("a\tb", bracketed: false), Is.EqualTo("a\tb"));

    /// <summary>
    /// Управляющие символы из буфера обмена не доходят до процесса: иначе текст
    /// со случайным ESC отдавал бы терминалу команды от имени пользователя.
    /// </summary>
    [Test]
    public void Prepare_ControlCharacters_AreDropped()
        => Assert.That(
            PasteText.Prepare("a\u001b[31mb\ac\0d", bracketed: false),
            Is.EqualTo("a[31mbcd"));

    /// <summary>
    /// От текста из одних управляющих символов не остаётся ничего — отправлять
    /// процессу пустую обёртку незачем.
    /// </summary>
    [Test]
    public void Prepare_OnlyControlCharacters_ReturnsEmpty()
        => Assert.That(PasteText.Prepare("\u001b\a\0", bracketed: true), Is.Empty);

    /// <summary>
    /// При включённом bracketed paste вставка уходит в обёртке: агент отличает
    /// её от набора руками и не отправляет запрос на каждой строке.
    /// </summary>
    [Test]
    public void Prepare_Bracketed_WrapsPayload()
    {
        var prepared = PasteText.Prepare("first\nsecond", bracketed: true);

        Assert.That(prepared, Is.EqualTo($"{PasteText.BracketStart}first\rsecond{PasteText.BracketEnd}"));
    }

    /// <summary>
    /// Маркер конца, попавший в буфер обмена, не закрывает обёртку раньше
    /// времени: остаток вставки иначе ушёл бы агенту как набранная команда.
    /// </summary>
    [Test]
    public void Prepare_Bracketed_PayloadCannotCloseWrapper()
        => Assert.That(
            PasteText.Prepare($"a{PasteText.BracketEnd}b", bracketed: true),
            Is.EqualTo($"{PasteText.BracketStart}a[201~b{PasteText.BracketEnd}"));
}
