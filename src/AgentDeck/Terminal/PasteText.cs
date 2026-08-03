using System.Text;

namespace AgentDeck.Terminal;

/// <summary>
/// Подготовка текста из буфера обмена ко вводу в PTY.
/// </summary>
public static class PasteText
{
    /// <summary>
    /// Начало вставки в режиме bracketed paste (DECSET 2004).
    /// </summary>
    public const string BracketStart = "\u001b[200~";

    /// <summary>
    /// Конец вставки в режиме bracketed paste.
    /// </summary>
    public const string BracketEnd = "\u001b[201~";

    /// <summary>
    /// Приводит текст к виду, пригодному для ввода в PTY: перевод строки — в CR
    /// (ведомый конец отдан в raw-режиме и ждёт именно его), управляющие символы
    /// — прочь. Отброс ESC заодно обезвреживает вставку: текст из буфера обмена
    /// не может ни закрыть bracketed paste раньше времени, ни отдать терминалу
    /// команду от имени пользователя.
    /// </summary>
    /// <param name="text">
    /// Текст из буфера обмена.
    /// </param>
    /// <param name="bracketed">
    /// Процесс включил bracketed paste и ждёт вставку в обёртке.
    /// </param>
    /// <returns>
    /// Готовая к отправке строка; пустая, если вставлять нечего.
    /// </returns>
    public static string Prepare(string? text, bool bracketed)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var body = new StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            var symbol = text[index];

            switch (symbol)
            {
                // CRLF — один перевод строки: вторым агент получил бы пустую
                // строку и отправил недописанный запрос.
                case '\r':
                    body.Append('\r');

                    if (index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    break;

                case '\n':
                    body.Append('\r');
                    break;

                case '\t':
                    body.Append('\t');
                    break;

                default:
                    if (!char.IsControl(symbol))
                    {
                        body.Append(symbol);
                    }

                    break;
            }
        }

        // От текста могли остаться одни управляющие символы: обёртка без
        // содержимого — та же пустая вставка, только видимая процессу.
        if (body.Length == 0)
        {
            return string.Empty;
        }

        return bracketed ? $"{BracketStart}{body}{BracketEnd}" : body.ToString();
    }
}
