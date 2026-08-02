using AgentDeck.Layout;

namespace AgentDeck.Session;

/// <summary>
/// Сохранённое состояние одного тайла. Процесс не сохраняется — тайл
/// восстанавливается плейсхолдером с префилленной директорией.
/// </summary>
public sealed class TileState
{
    /// <summary>
    /// Идентификатор тайла; совпадает с листом дерева раскладки.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Рабочая директория тайла.
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>
    /// Имя утилиты, запущенной в прошлой сессии; её кнопка акцентируется.
    /// </summary>
    public string? Utility { get; set; }

    /// <summary>
    /// Элемент перечисления CLI из сессий старого формата. Только для чтения
    /// сохранённых файлов: новые сессии пишут <see cref="Utility"/>.
    /// </summary>
    public string? AgentKind { get; set; }
}

/// <summary>
/// Сохранённые размер и положение окна.
/// </summary>
public sealed class WindowState
{
    /// <summary>
    /// Координата X окна на экране.
    /// </summary>
    public int? X { get; set; }

    /// <summary>
    /// Координата Y окна на экране.
    /// </summary>
    public int? Y { get; set; }

    /// <summary>
    /// Ширина окна.
    /// </summary>
    public double? Width { get; set; }

    /// <summary>
    /// Высота окна.
    /// </summary>
    public double? Height { get; set; }

    /// <summary>
    /// Окно было развёрнуто на весь экран.
    /// </summary>
    public bool Maximized { get; set; }
}

/// <summary>
/// Состояние сессии целиком: раскладка, тайлы и геометрия окна.
/// </summary>
public sealed class SessionState
{
    /// <summary>
    /// Версия формата раскладки; чужая версия отбрасывает весь файл.
    /// </summary>
    public int LayoutVersion { get; set; }

    /// <summary>
    /// Корень дерева раскладки.
    /// </summary>
    public LayoutNodeDto? Layout { get; set; }

    /// <summary>
    /// Тайлы дека.
    /// </summary>
    public List<TileState> Tiles { get; set; } = [];

    /// <summary>
    /// Геометрия окна.
    /// </summary>
    public WindowState? Window { get; set; }
}
