using AgentDeck.Layout;
using NUnit.Framework;

namespace AgentDeck.Tests.Layout;

/// <summary>
/// Общие проверки инвариантов раскладки для тестов.
/// </summary>
internal static class LayoutTestHelpers
{
    /// <summary>
    /// Допуск сравнения геометрии в нормированных координатах.
    /// </summary>
    public const double Eps = 1e-9;

    /// <summary>
    /// Проверяет полный набор инвариантов: структура дерева, полное покрытие
    /// единичной области и отсутствие попарных пересечений.
    /// </summary>
    public static void AssertInvariants(LayoutTree tree, string context = "")
    {
        var suffix = string.IsNullOrEmpty(context) ? string.Empty : $" [{context}]";

        Assert.That(tree.Validate(), Is.True, $"структурные инварианты нарушены{suffix}");

        var rects = tree.Project();

        if (rects.Count == 0)
        {
            Assert.That(tree.Root, Is.Null, $"пустая проекция при непустом дереве{suffix}");
            return;
        }

        var area = rects.Sum(r => r.Rect.Area);
        Assert.That(area, Is.EqualTo(1.0).Within(1e-9), $"проекция не покрывает область целиком{suffix}");

        for (var i = 0; i < rects.Count; i++)
        {
            var rect = rects[i].Rect;

            Assert.That(rect.W, Is.GreaterThan(0), $"нулевая ширина тайла{suffix}");
            Assert.That(rect.H, Is.GreaterThan(0), $"нулевая высота тайла{suffix}");
            Assert.That(rect.X, Is.GreaterThanOrEqualTo(-Eps), $"выход за левую границу{suffix}");
            Assert.That(rect.Y, Is.GreaterThanOrEqualTo(-Eps), $"выход за верхнюю границу{suffix}");
            Assert.That(rect.Right, Is.LessThanOrEqualTo(1 + Eps), $"выход за правую границу{suffix}");
            Assert.That(rect.Bottom, Is.LessThanOrEqualTo(1 + Eps), $"выход за нижнюю границу{suffix}");

            for (var j = i + 1; j < rects.Count; j++)
            {
                Assert.That(
                    rect.IntersectsArea(rects[j].Rect, 1e-9),
                    Is.False,
                    $"тайлы пересекаются: {rect} и {rects[j].Rect}{suffix}");
            }
        }
    }

    /// <summary>
    /// Строит дерево из указанного числа тайлов через автоматическое размещение.
    /// </summary>
    public static (LayoutTree Tree, List<Guid> Ids) BuildAuto(int count)
    {
        var tree = new LayoutTree();
        var ids = new List<Guid>();

        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            Assert.That(tree.AddTileAuto(id), Is.True, $"не удалось добавить тайл #{i}");
            ids.Add(id);
        }

        return (tree, ids);
    }

    /// <summary>
    /// Возвращает прямоугольник тайла из проекции.
    /// </summary>
    public static RectD Rect(this LayoutTree tree, Guid tileId)
    {
        var rect = tree.RectOf(tileId, RectD.Unit);
        Assert.That(rect, Is.Not.Null, "тайл отсутствует в проекции");
        return rect!.Value;
    }
}
