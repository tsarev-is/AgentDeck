using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgentDeck.Models;

/// <summary>
/// Открытие рабочей папки тайла в файловом менеджере рабочего стола.
/// </summary>
public static class FileManager
{
    /// <summary>
    /// Открывает директорию в файловом менеджере по умолчанию.
    /// </summary>
    /// <param name="directory">
    /// Путь к папке; «~» раскрывается.
    /// </param>
    /// <returns>
    /// false, если открывать нечего или менеджер не запустился.
    /// </returns>
    public static bool Open(string? directory)
    {
        var expanded = PathUtilities.ExpandHome(directory);

        // Папки может и не быть: на плейсхолдере путь набирают руками, а
        // запущенный процесс свою рабочую папку мог и удалить.
        if (expanded.Length == 0 || !System.IO.Directory.Exists(expanded))
        {
            return false;
        }

        var (app, arguments) = Command(expanded);
        var start = new ProcessStartInfo(app) { UseShellExecute = false };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // Менеджера в системе нет. Сказать об этом негде — в заголовке тайла
            // места под сообщение нет, — так что просто не открываем ничего.
            return false;
        }
    }

    /// <summary>
    /// Команда, открывающая папку в файловом менеджере текущей системы.
    /// </summary>
    /// <param name="directory">
    /// Путь к папке.
    /// </param>
    /// <returns>
    /// Исполняемый файл и его аргументы.
    /// </returns>
    public static (string App, IReadOnlyList<string> Arguments) Command(string directory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("explorer.exe", [directory]);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return ("open", [directory]);
        }

        // xdg-open уважает выбор пользователя из xdg-mime: dolphin, nautilus,
        // thunar — что настроено, то и откроется.
        return ("xdg-open", [directory]);
    }
}
