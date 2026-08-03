namespace AgentDeck.Terminal;

/// <summary>
/// Действие с буфером обмена, к которому сводится нажатие клавиш в терминале.
/// </summary>
public enum ClipboardAction
{
    /// <summary>
    /// Сочетание к буферу обмена не относится — клавиши уходят процессу.
    /// </summary>
    None,

    /// <summary>
    /// Скопировать выделение в буфер обмена.
    /// </summary>
    Copy,

    /// <summary>
    /// Вставить текст из буфера обмена в ввод процесса.
    /// </summary>
    Paste,
}
