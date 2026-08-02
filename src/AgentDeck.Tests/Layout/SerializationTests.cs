using AgentDeck.Layout;
using NUnit.Framework;

namespace AgentDeck.Tests.Layout;

/// <summary>
/// Сериализация раскладки: round-trip и отказ на повреждённых данных.
/// </summary>
[TestFixture]
public class SerializationTests
{
    /// <summary>
    /// Round-trip сохраняет форму дерева, доли и идентификаторы тайлов.
    /// </summary>
    [Test]
    public void RoundTrip_PreservesShapeRatiosAndIds()
    {
        var (tree, ids) = LayoutTestHelpers.BuildAuto(6);
        tree.ResizeSplitter((SplitNode)tree.Root!, 0, 0.07);

        var restored = LayoutSerializer.Deserialize(LayoutSerializer.Serialize(tree));

        Assert.That(restored, Is.Not.Null);

        var original = tree.Project();
        var actual = restored!.Project();

        Assert.That(actual, Has.Count.EqualTo(original.Count));

        foreach (var id in ids)
        {
            Assert.That(restored.Rect(id), Is.EqualTo(tree.Rect(id)), $"прямоугольник тайла {id}");
        }
    }

    /// <summary>
    /// Round-trip пустого дерева даёт пустое дерево.
    /// </summary>
    [Test]
    public void RoundTrip_EmptyTree_StaysEmpty()
    {
        var restored = LayoutSerializer.Deserialize(LayoutSerializer.Serialize(new LayoutTree()));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Root, Is.Null);
    }

    /// <summary>
    /// Мусорный, обрезанный и пустой JSON дают null.
    /// </summary>
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("не json вовсе")]
    [TestCase("{\"layoutVersion\":1,\"root\":{")]
    [TestCase("[1,2,3]")]
    public void Deserialize_Garbage_ReturnsNull(string json)
    {
        Assert.That(LayoutSerializer.Deserialize(json), Is.Null);
    }

    /// <summary>
    /// Чужая версия формата отбрасывается целиком.
    /// </summary>
    [TestCase(0)]
    [TestCase(2)]
    [TestCase(-1)]
    public void Deserialize_ForeignVersion_ReturnsNull(int version)
    {
        var json = $$"""
        {
          "layoutVersion": {{version}},
          "root": { "tileId": "{{Guid.NewGuid()}}", "ratio": 1 }
        }
        """;

        Assert.That(LayoutSerializer.Deserialize(json), Is.Null);
    }

    /// <summary>
    /// Доли, не суммирующиеся в единицу, отбрасываются.
    /// </summary>
    [Test]
    public void Deserialize_RatiosNotSummingToOne_ReturnsNull()
    {
        var json = Json(0.3, 0.3);

        Assert.That(LayoutSerializer.Deserialize(json), Is.Null);
    }

    /// <summary>
    /// Почти единичная сумма нормализуется, а не отбрасывается.
    /// </summary>
    [Test]
    public void Deserialize_NearlyOneSum_IsNormalized()
    {
        var restored = LayoutSerializer.Deserialize(Json(0.4999, 0.5))!;

        Assert.That(restored, Is.Not.Null);

        var split = (SplitNode)restored.Root!;
        Assert.That(split.Children.Sum(c => c.Ratio), Is.EqualTo(1.0).Within(1e-12));
        Assert.That(restored.Validate(), Is.True);
    }

    /// <summary>
    /// Отрицательная и нулевая доля отбрасываются.
    /// </summary>
    [TestCase(-0.5, 1.5)]
    [TestCase(0.0, 1.0)]
    public void Deserialize_NonPositiveRatio_ReturnsNull(double first, double second)
    {
        Assert.That(LayoutSerializer.Deserialize(Json(first, second)), Is.Null);
    }

    /// <summary>
    /// Повторяющийся идентификатор тайла отбрасывается.
    /// </summary>
    [Test]
    public void Deserialize_DuplicateTileId_ReturnsNull()
    {
        var id = Guid.NewGuid();

        var json = $$"""
        {
          "layoutVersion": 1,
          "root": {
            "orientation": "Horizontal",
            "ratio": 1,
            "children": [
              { "tileId": "{{id}}", "ratio": 0.5 },
              { "tileId": "{{id}}", "ratio": 0.5 }
            ]
          }
        }
        """;

        Assert.That(LayoutSerializer.Deserialize(json), Is.Null);
    }

    /// <summary>
    /// Нераспознаваемый идентификатор тайла отбрасывается.
    /// </summary>
    [Test]
    public void Deserialize_MalformedTileId_ReturnsNull()
    {
        var json = """
        { "layoutVersion": 1, "root": { "tileId": "не-guid", "ratio": 1 } }
        """;

        Assert.That(LayoutSerializer.Deserialize(json), Is.Null);
    }

    /// <summary>
    /// Сплит с одним ребёнком или без ориентации отбрасывается.
    /// </summary>
    [Test]
    public void Deserialize_DegenerateSplit_ReturnsNull()
    {
        var single = $$"""
        {
          "layoutVersion": 1,
          "root": {
            "orientation": "Horizontal",
            "ratio": 1,
            "children": [ { "tileId": "{{Guid.NewGuid()}}", "ratio": 1 } ]
          }
        }
        """;

        var noOrientation = $$"""
        {
          "layoutVersion": 1,
          "root": {
            "ratio": 1,
            "children": [
              { "tileId": "{{Guid.NewGuid()}}", "ratio": 0.5 },
              { "tileId": "{{Guid.NewGuid()}}", "ratio": 0.5 }
            ]
          }
        }
        """;

        Assert.Multiple(() =>
        {
            Assert.That(LayoutSerializer.Deserialize(single), Is.Null);
            Assert.That(LayoutSerializer.Deserialize(noOrientation), Is.Null);
        });
    }

    /// <summary>
    /// Сохранённый JSON содержит версию формата.
    /// </summary>
    [Test]
    public void Serialize_WritesCurrentVersion()
    {
        var (tree, _) = LayoutTestHelpers.BuildAuto(2);

        Assert.That(LayoutSerializer.Serialize(tree), Does.Contain($"\"layoutVersion\": {LayoutSerializer.CurrentVersion}"));
    }

    /// <summary>
    /// Строит JSON двухлистового горизонтального сплита с заданными долями.
    /// </summary>
    private static string Json(double first, double second) => $$"""
    {
      "layoutVersion": 1,
      "root": {
        "orientation": "Horizontal",
        "ratio": 1,
        "children": [
          { "tileId": "{{Guid.NewGuid()}}", "ratio": {{first.ToString(System.Globalization.CultureInfo.InvariantCulture)}} },
          { "tileId": "{{Guid.NewGuid()}}", "ratio": {{second.ToString(System.Globalization.CultureInfo.InvariantCulture)}} }
        ]
      }
    }
    """;
}
