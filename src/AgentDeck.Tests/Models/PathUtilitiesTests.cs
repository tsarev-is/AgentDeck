using AgentDeck.Models;
using NUnit.Framework;

namespace AgentDeck.Tests.Models;

/// <summary>
/// Операции над путями: раскрытие и свёртка «~», подъём на уровень выше,
/// дописывание вложенной папки.
/// </summary>
[TestFixture]
public class PathUtilitiesTests
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static char Slash => Path.DirectorySeparatorChar;

    /// <summary>
    /// Путь внутри домашней папки показывается через «~».
    /// </summary>
    [Test]
    public void CollapseHome_ShortensPathsInsideHome()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PathUtilities.CollapseHome(Home), Is.EqualTo("~"));
            Assert.That(PathUtilities.CollapseHome(Path.Combine(Home, "dev", "core")), Is.EqualTo($"~{Slash}dev{Slash}core"));
            Assert.That(PathUtilities.CollapseHome(string.Empty), Is.Empty);
        });
    }

    /// <summary>
    /// Сосед домашней папки внутрь неё не попадает: «/home/user2» — не «~2».
    /// </summary>
    [Test]
    public void CollapseHome_LeavesSiblingOfHomeAlone()
    {
        var sibling = Home + "2";

        Assert.That(PathUtilities.CollapseHome(sibling), Is.EqualTo(sibling));
    }

    /// <summary>
    /// Свёртка и раскрытие возвращают исходный путь.
    /// </summary>
    [Test]
    public void CollapseHome_RoundTripsThroughExpandHome()
    {
        var path = Path.Combine(Home, "dev", "core");

        Assert.That(PathUtilities.ExpandHome(PathUtilities.CollapseHome(path)), Is.EqualTo(path));
    }

    /// <summary>
    /// Подъём сохраняет вид пути: «~/dev/core» превращается в «~/dev».
    /// </summary>
    [Test]
    public void Parent_KeepsTildeForm()
    {
        Assert.That(PathUtilities.Parent("~/dev/core"), Is.EqualTo($"~{Slash}dev"));
    }

    /// <summary>
    /// Хвостовой разделитель не возвращает тот же каталог обратно.
    /// </summary>
    [Test]
    public void Parent_IgnoresTrailingSeparator()
    {
        // Разделители исходного пути сохраняются как есть — и на Windows тоже.
        Assert.That(PathUtilities.Parent("/tmp/work/"), Is.EqualTo("/tmp"));
    }

    /// <summary>
    /// Из домашней папки поднимаемся в её реальный родитель.
    /// </summary>
    [Test]
    public void Parent_FromHome_GoesToRealParent()
    {
        Assert.That(PathUtilities.Parent("~"), Is.EqualTo(PathUtilities.CollapseHome(Path.GetDirectoryName(Home)!)));
    }

    /// <summary>
    /// Из корня и из пустого пути подниматься некуда — кнопка «вверх» гаснет.
    /// </summary>
    [Test]
    public void Parent_AtRoot_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PathUtilities.Parent(Path.GetPathRoot(Home)), Is.Null);
            Assert.That(PathUtilities.Parent(string.Empty), Is.Null);
            Assert.That(PathUtilities.Parent("   "), Is.Null);
            Assert.That(PathUtilities.Parent("dev"), Is.Null, "относительный путь из одного сегмента");
        });
    }

    /// <summary>
    /// Вложенная папка дописывается разделителем самого пути, «~» не раскрывается.
    /// </summary>
    [Test]
    public void Child_AppendsSegmentPreservingPathForm()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PathUtilities.Child("~/dev", "core"), Is.EqualTo("~/dev/core"));
            Assert.That(PathUtilities.Child("~/dev/", "core"), Is.EqualTo("~/dev/core"));
            Assert.That(PathUtilities.Child("/", "tmp"), Is.EqualTo("/tmp"));
            Assert.That(PathUtilities.Child(@"C:\Users", "dev"), Is.EqualTo(@"C:\Users\dev"));
            Assert.That(PathUtilities.Child(@"C:\", "dev"), Is.EqualTo(@"C:\dev"));
        });
    }

    /// <summary>
    /// Пустое имя папки путь не меняет.
    /// </summary>
    [Test]
    public void Child_EmptyName_KeepsDirectory()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PathUtilities.Child("~/dev", string.Empty), Is.EqualTo("~/dev"));
            Assert.That(PathUtilities.Child("~/dev", null), Is.EqualTo("~/dev"));
            Assert.That(PathUtilities.Child(string.Empty, "dev"), Is.EqualTo("dev"));
        });
    }

    /// <summary>
    /// Имя папки дописывается дословно: пробелы по краям — законная часть имени
    /// на Unix, и обрезка увела бы путь в чужой каталог. Имена приходят из
    /// файловой системы, а не из поля ввода, и «причёсывать» их нечего.
    /// </summary>
    [Test]
    public void Child_KeepsFolderNameVerbatim()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PathUtilities.Child("/work", " spaced "), Is.EqualTo("/work/ spaced "));
            Assert.That(PathUtilities.Child("~/dev", "  "), Is.EqualTo("~/dev/  "));
            Assert.That(PathUtilities.Child("/work", "trailing "), Is.EqualTo("/work/trailing "));
        });
    }
}
