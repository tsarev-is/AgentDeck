namespace AgentDeck.Models;

/// <summary>
/// Разбор пользовательской команды в том объёме, который нужен приложению:
/// найти в ней имя исполняемого файла так же, как это сделает shell.
/// </summary>
public static class ShellCommand
{
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
    {
        var trimmed = (command ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        foreach (var token in trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsAssignment(token))
            {
                return token;
            }
        }

        return null;
    }

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
