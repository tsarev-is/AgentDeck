using Avalonia.Input;

namespace AgentDeck.Terminal;

/// <summary>
/// Разбор клавиатурных сочетаний терминала, относящихся к буферу обмена.
/// </summary>
public static class ClipboardGesture
{
    /// <summary>
    /// Определяет, что нажатие делает с буфером обмена. Ctrl+C в терминале —
    /// прерывание процесса, поэтому копированием он становится только при живом
    /// выделении; Shift снимает эту оговорку, а Insert-пары остаются от привычки.
    /// </summary>
    /// <param name="key">
    /// Нажатая клавиша.
    /// </param>
    /// <param name="modifiers">
    /// Нажатые модификаторы.
    /// </param>
    /// <param name="hasSelection">
    /// В буфере терминала есть выделенный текст.
    /// </param>
    /// <returns>
    /// Действие с буфером обмена; <see cref="ClipboardAction.None"/> — сочетание
    /// принадлежит процессу и перехватывать его нельзя.
    /// </returns>
    public static ClipboardAction Resolve(Key key, KeyModifiers modifiers, bool hasSelection)
    {
        // Alt и Meta меняют смысл сочетания: первый уходит процессу как
        // meta-последовательность, второй принадлежит оконному менеджеру.
        if ((modifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) != 0)
        {
            return ClipboardAction.None;
        }

        var control = (modifiers & KeyModifiers.Control) != 0;
        var shift = (modifiers & KeyModifiers.Shift) != 0;

        return key switch
        {
            // Без выделения Ctrl+C обязан остаться SIGINT: единственный способ
            // прервать разогнавшегося агента отбирать нельзя.
            Key.C when control && (shift || hasSelection) => ClipboardAction.Copy,
            Key.V when control => ClipboardAction.Paste,
            Key.Insert when control && !shift => ClipboardAction.Copy,
            Key.Insert when shift && !control => ClipboardAction.Paste,
            _ => ClipboardAction.None,
        };
    }
}
