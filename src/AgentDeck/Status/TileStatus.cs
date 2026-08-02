namespace AgentDeck.Status;

/// <summary>
/// Состояние тайла, определяющее вид точки статуса и тонировку рамки.
/// </summary>
public enum TileStatus
{
    /// <summary>
    /// Процесс не запущен — тайл показывает плейсхолдер.
    /// </summary>
    Placeholder,

    /// <summary>
    /// Процесс работает и выдаёт вывод.
    /// </summary>
    Running,

    /// <summary>
    /// Процесс притих и ждёт ввода пользователя.
    /// </summary>
    AwaitingInput,

    /// <summary>
    /// Агент запросил подтверждение действия.
    /// </summary>
    AwaitingPermission,

    /// <summary>
    /// Процесс завершился с нулевым кодом.
    /// </summary>
    Finished,

    /// <summary>
    /// Процесс завершился с ненулевым кодом.
    /// </summary>
    Crashed,
}
