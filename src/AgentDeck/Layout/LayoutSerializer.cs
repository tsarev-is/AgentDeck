using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentDeck.Layout;

/// <summary>
/// Сериализуемое представление узла дерева раскладки.
/// </summary>
public sealed class LayoutNodeDto
{
    /// <summary>
    /// Идентификатор тайла; заполнен только у листа.
    /// </summary>
    public string? TileId { get; set; }

    /// <summary>
    /// Ориентация сплита; заполнена только у сплита.
    /// </summary>
    public string? Orientation { get; set; }

    /// <summary>
    /// Доля области родителя вдоль его оси.
    /// </summary>
    public double Ratio { get; set; } = 1.0;

    /// <summary>
    /// Дети сплита.
    /// </summary>
    public List<LayoutNodeDto>? Children { get; set; }
}

/// <summary>
/// Сериализуемый документ раскладки с версией формата.
/// </summary>
public sealed class LayoutDocumentDto
{
    /// <summary>
    /// Версия формата раскладки.
    /// </summary>
    public int LayoutVersion { get; set; }

    /// <summary>
    /// Корень дерева; null — пустой дек.
    /// </summary>
    public LayoutNodeDto? Root { get; set; }
}

/// <summary>
/// Сериализация дерева раскладки. Любое повреждение данных даёт null —
/// вызывающий код переходит к дефолтной раскладке.
/// </summary>
public static class LayoutSerializer
{
    /// <summary>
    /// Текущая версия формата раскладки.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Параметры JSON, общие для чтения и записи.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>
    /// Сериализует дерево в JSON.
    /// </summary>
    public static string Serialize(LayoutTree tree) => JsonSerializer.Serialize(ToDto(tree), JsonOptions);

    /// <summary>
    /// Разбирает JSON в дерево; возвращает null при любом повреждении.
    /// </summary>
    public static LayoutTree? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        LayoutDocumentDto? document;

        try
        {
            document = JsonSerializer.Deserialize<LayoutDocumentDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        return FromDto(document);
    }

    /// <summary>
    /// Строит DTO дерева.
    /// </summary>
    public static LayoutDocumentDto ToDto(LayoutTree tree)
        => new() { LayoutVersion = CurrentVersion, Root = tree.Root is null ? null : ToDto(tree.Root, 1.0) };

    /// <summary>
    /// Восстанавливает дерево из DTO; возвращает null при несовместимой версии
    /// или нарушении структуры.
    /// </summary>
    public static LayoutTree? FromDto(LayoutDocumentDto? document)
    {
        if (document is null || document.LayoutVersion != CurrentVersion)
        {
            return null;
        }

        if (document.Root is null)
        {
            return new LayoutTree();
        }

        var seen = new HashSet<Guid>();
        var root = FromDto(document.Root, seen);
        if (root is null)
        {
            return null;
        }

        var tree = new LayoutTree(root);
        return tree.Validate() ? tree : null;
    }

    private static LayoutNodeDto ToDto(LayoutNode node, double ratio)
    {
        if (node is LeafNode leaf)
        {
            return new LayoutNodeDto { TileId = leaf.TileId.ToString(), Ratio = ratio };
        }

        var split = (SplitNode)node;

        return new LayoutNodeDto
        {
            Orientation = split.Orientation.ToString(),
            Ratio = ratio,
            Children = [.. split.Children.Select(c => ToDto(c.Node, c.Ratio))],
        };
    }

    private static LayoutNode? FromDto(LayoutNodeDto dto, HashSet<Guid> seen)
    {
        if (dto.TileId is not null)
        {
            return dto.Children is { Count: > 0 } || !Guid.TryParse(dto.TileId, out var tileId) || !seen.Add(tileId)
                ? null
                : new LeafNode(tileId);
        }

        if (dto.Children is not { Count: >= 2 } || !Enum.TryParse<Orientation>(dto.Orientation, out var orientation))
        {
            return null;
        }

        var children = new List<SplitChild>(dto.Children.Count);

        foreach (var childDto in dto.Children)
        {
            if (!double.IsFinite(childDto.Ratio) || childDto.Ratio <= 0)
            {
                return null;
            }

            var child = FromDto(childDto, seen);
            if (child is null)
            {
                return null;
            }

            children.Add(new SplitChild(child, childDto.Ratio));
        }

        // Доли обязаны суммироваться в единицу; расхождение в пределах допуска нормализуется.
        var sum = children.Sum(c => c.Ratio);
        if (Math.Abs(sum - 1.0) > LayoutConstants.RatioTolerance)
        {
            return null;
        }

        if (Math.Abs(sum - 1.0) > LayoutConstants.Epsilon)
        {
            children = [.. children.Select(c => c with { Ratio = c.Ratio / sum })];
        }

        return new SplitNode(orientation, children);
    }
}
