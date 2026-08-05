using System.Text;

namespace AgentDeck.Models;

/// <summary>
/// Разбор пользовательской команды в том объёме, который нужен приложению:
/// найти в ней имена исполняемых файлов так же, как это сделает shell.
/// </summary>
public static class ShellCommand
{
    /// <summary>
    /// Команды, которые запускают переданную им команду: утилиту в такой строке
    /// надо искать среди их аргументов, а сама обёртка о запущенном ничего не
    /// говорит.
    /// </summary>
    private static readonly string[] Wrappers =
    [
        "sudo", "doas", "env", "exec", "command", "nohup", "nice", "time", "stdbuf",
        "npx", "bunx", "pnpm", "yarn", "uv", "uvx",
    ];

    /// <summary>
    /// Первый токен, который shell будет искать как исполняемый файл. Ведущие
    /// присваивания окружения пропускаются: в <c>FOO=bar cli --flag</c> запускается
    /// <c>cli</c>, а <c>FOO=bar</c> — лишь префикс команды.
    /// </summary>
    /// <param name="command">
    /// Команда утилиты как её ввёл пользователь.
    /// </param>
    /// <returns>
    /// null, если исполняемого токена нет: команда пуста или состоит из одних
    /// присваиваний (<c>FOO=bar</c> — валидное выражение shell без запуска).
    /// </returns>
    public static string? ExecutableToken(string? command)
        => ExecutableTokens(command).FirstOrDefault();

    /// <summary>
    /// Токены команды, стоящие на позициях, где shell ищет исполняемый файл.
    /// </summary>
    /// <param name="command">
    /// Команда утилиты как её ввёл пользователь.
    /// </param>
    /// <returns>
    /// Токены в порядке появления; пустая последовательность для пустой команды.
    /// </returns>
    /// <remarks>
    /// Первого токена недостаточно: запуск бывает цепочкой
    /// (<c>mkdir -p "$dir" &amp;&amp; cd "$dir" &amp;&amp; codex</c>) или идёт через обёртку
    /// (<c>npx claude</c>, <c>sudo -u dev codex</c>), и утилита стоит не первой.
    /// Но и всякое слово командой не является: в <c>ssh agent</c> запускается ssh,
    /// а в <c>bash -c "echo claude"</c> — bash, и принять их аргументы за имя
    /// утилиты значит выдать обычному терминалу чужие паттерны статуса. Поэтому
    /// команда ищется там, где её ищет shell: в начале строки, после
    /// разделителей (<c>&amp;&amp;</c>, <c>||</c>, <c>;</c>, <c>|</c>, подстановки,
    /// перевода строки) и в аргументах обёрток. Ведущие присваивания окружения
    /// пропускаются на каждой такой позиции.
    /// </remarks>
    public static IEnumerable<string> ExecutableTokens(string? command)
    {
        // Аргументы обёртки перебираются целиком: свои флаги каждая описывает
        // по-своему («sudo -u dev codex»), и отличить флаг от команды, не зная
        // грамматики обёртки, нельзя.
        var wrapped = false;
        var executable = true;

        foreach (var (word, afterSeparator) in Tokens(command))
        {
            if (afterSeparator)
            {
                wrapped = false;
                executable = true;
            }

            if (!executable || IsAssignment(word))
            {
                continue;
            }

            yield return word;

            wrapped |= IsWrapper(word);
            executable = wrapped;
        }
    }

    /// <summary>
    /// Возвращает имя файла из слова команды без пути и расширения:
    /// «~/.local/bin/codex» → «codex», «cursor-agent.exe» → «cursor-agent».
    /// </summary>
    /// <param name="word">
    /// Слово команды.
    /// </param>
    public static string Stem(string word) => Path.GetFileNameWithoutExtension(word);

    /// <summary>
    /// Разбирает команду на слова, отмечая те, перед которыми стоял разделитель
    /// команд.
    /// </summary>
    /// <param name="command">
    /// Команда утилиты как её ввёл пользователь.
    /// </param>
    /// <returns>
    /// Слова без кавычек в порядке появления; <c>AfterSeparator</c> — признак
    /// того, что слово стоит на месте новой команды.
    /// </returns>
    /// <remarks>
    /// Кавычки не разделяют слова, а скрывают внутри себя всё, включая
    /// разделители: <c>bash -c "echo claude"</c> запускает одну команду, а не
    /// две. Подстановка (<c>$(…)</c>, обратные кавычки), наоборот, начинается с
    /// команды, поэтому её скобки считаются разделителями.
    /// </remarks>
    private static IEnumerable<(string Word, bool AfterSeparator)> Tokens(string? command)
    {
        var word = new StringBuilder();
        var quote = '\0';
        var separated = true;

        foreach (var symbol in command ?? string.Empty)
        {
            if (quote is not '\0')
            {
                if (symbol == quote)
                {
                    quote = '\0';
                }
                else
                {
                    word.Append(symbol);
                }

                continue;
            }

            if (symbol is '"' or '\'')
            {
                quote = symbol;
                continue;
            }

            var separator = symbol is '&' or '|' or ';' or '(' or ')' or '`' or '\n' or '\r';

            if (!separator && !char.IsWhiteSpace(symbol))
            {
                word.Append(symbol);
                continue;
            }

            if (word.Length > 0)
            {
                yield return (word.ToString(), separated);
                word.Clear();
                separated = false;
            }

            separated |= separator;
        }

        if (word.Length > 0)
        {
            yield return (word.ToString(), separated);
        }
    }

    /// <summary>
    /// Слово запускает другую команду, а не программу тайла.
    /// </summary>
    /// <param name="word">
    /// Слово команды.
    /// </param>
    private static bool IsWrapper(string word)
        => Wrappers.Contains(Stem(word), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Токен вида <c>NAME=value</c>, где NAME — допустимое имя переменной shell.
    /// </summary>
    private static bool IsAssignment(string token)
    {
        var separator = token.IndexOf('=');

        if (separator <= 0)
        {
            return false;
        }

        for (var i = 0; i < separator; i++)
        {
            var symbol = token[i];

            // Имя переменной — только ASCII-буквы, цифры и подчёркивание,
            // причём с цифры оно начинаться не может.
            if (!char.IsAsciiLetter(symbol) && symbol != '_' && !(i > 0 && char.IsAsciiDigit(symbol)))
            {
                return false;
            }
        }

        return true;
    }
}
