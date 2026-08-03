using System.Runtime.Versioning;
using AgentDeck.Models;
using NUnit.Framework;

namespace AgentDeck.Tests.Models;

/// <summary>
/// Открытие папки в файловом менеджере: выбор команды и отказ открывать то,
/// чего нет.
/// </summary>
[TestFixture]
public class FileManagerTests
{
    /// <summary>
    /// На Linux папку открывает xdg-open — он и уважает выбор пользователя.
    /// </summary>
    [Test]
    [Platform("Linux")]
    public void Command_OnLinux_UsesXdgOpen()
    {
        var (app, arguments) = FileManager.Command("/work/api");

        Assert.Multiple(() =>
        {
            Assert.That(app, Is.EqualTo("xdg-open"));
            Assert.That(arguments, Is.EqualTo(new[] { "/work/api" }));
        });
    }

    /// <summary>
    /// Путь уходит отдельным аргументом, а не частью командной строки: пробелы
    /// и кавычки в имени папки не должны разваливать команду.
    /// </summary>
    [Test]
    public void Command_KeepsPathAsSingleArgument()
    {
        var (_, arguments) = FileManager.Command("/work/my project");

        Assert.That(arguments, Is.EqualTo(new[] { "/work/my project" }));
    }

    /// <summary>
    /// Несуществующую папку не открываем: на плейсхолдере путь набирают руками,
    /// и по дороге он законно бывает неполным.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("/no/such/directory-8f2a1c")]
    public void Open_WithoutDirectory_DoesNothing(string? directory)
    {
        Assert.That(FileManager.Open(directory), Is.False);
    }

    /// <summary>
    /// Существующая папка уходит в файловый менеджер целиком и одним аргументом.
    /// Менеджер подменён скриптом в PATH: проверяем запуск, а не чужое окно.
    /// </summary>
    [Test]
    [Platform("Linux")]
    [SupportedOSPlatform("linux")]
    public async Task Open_ExistingDirectory_LaunchesFileManager()
    {
        var root = Directory.CreateTempSubdirectory("agentdeck-open-");
        var target = root.CreateSubdirectory("project dir");
        var log = Path.Combine(root.FullName, "opened.txt");
        var path = Environment.GetEnvironmentVariable("PATH");

        await File.WriteAllTextAsync(
            Path.Combine(root.FullName, "xdg-open"),
            $"#!/bin/sh\nprintf '%s' \"$1\" > '{log}'\n");

        File.SetUnixFileMode(
            Path.Combine(root.FullName, "xdg-open"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{root.FullName}:{path}");

            Assert.That(FileManager.Open(target.FullName), Is.True);
            Assert.That(await WaitForFile(log), Is.EqualTo(target.FullName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", path);
            root.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Файл — не папка: открывать его файловым менеджером тайл не просит.
    /// </summary>
    [Test]
    public void Open_File_DoesNothing()
    {
        var file = Path.GetTempFileName();

        try
        {
            Assert.That(FileManager.Open(file), Is.False);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Ждёт, пока запущенный процесс не оставит след.
    /// </summary>
    /// <param name="path">
    /// Файл, в который пишет подменённый менеджер.
    /// </param>
    /// <returns>
    /// Содержимое файла или пустая строка, если процесс так и не отписался.
    /// </returns>
    private static async Task<string> WaitForFile(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path);
            }

            await Task.Delay(20);
        }

        return string.Empty;
    }
}
