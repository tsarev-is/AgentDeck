namespace AgentDeck.Terminal;

/// <summary>
/// Накопитель вывода PTY между разгребаниями в UI-потоке. Процесс пишет
/// быстрее, чем VT-парсер разбирает (замер: ~300 МБ/с против единиц МБ/с),
/// поэтому чанки копятся здесь, а в диспетчер уходит одна задача на пачку.
/// Сверх потолка выбрасываются самые старые чанки: они всё равно ушли бы за
/// пределы scrollback, а неограниченная очередь съела бы память и UI-поток.
/// </summary>
public sealed class OutputBuffer
{
    /// <summary>
    /// Потолок накопленного вывода. Экран терминала — десятки килобайт,
    /// так что два мегабайта переживают любую нормальную пачку.
    /// </summary>
    public const int DefaultCapacityBytes = 2 * 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly Queue<byte[]> _chunks = [];
    private readonly int _capacityBytes;

    private long _pendingBytes;
    private long _droppedBytes;
    private bool _drainScheduled;

    /// <summary>
    /// Создаёт накопитель.
    /// </summary>
    /// <param name="capacityBytes">
    /// Потолок накопленного; по умолчанию 2 МБ.
    /// </param>
    public OutputBuffer(int? capacityBytes = null)
    {
        _capacityBytes = Math.Max(1, capacityBytes ?? DefaultCapacityBytes);
    }

    /// <summary>
    /// Сколько байт ждёт разгребания.
    /// </summary>
    public long PendingBytes
    {
        get
        {
            lock (_gate)
            {
                return _pendingBytes;
            }
        }
    }

    /// <summary>
    /// Сколько байт выброшено по переполнению за всё время.
    /// </summary>
    public long DroppedBytes
    {
        get
        {
            lock (_gate)
            {
                return _droppedBytes;
            }
        }
    }

    /// <summary>
    /// Добавляет чанк вывода.
    /// </summary>
    /// <param name="chunk">
    /// Прочитанные из PTY байты.
    /// </param>
    /// <returns>
    /// true, если разгребание нужно запланировать; false — оно уже запланировано
    /// и подхватит этот чанк.
    /// </returns>
    public bool Append(byte[] chunk)
    {
        if (chunk.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            _chunks.Enqueue(chunk);
            _pendingBytes += chunk.Length;

            // Последний чанк не выбрасываем: на экране должен остаться хвост вывода.
            while (_pendingBytes > _capacityBytes && _chunks.Count > 1)
            {
                var dropped = _chunks.Dequeue();
                _pendingBytes -= dropped.Length;
                _droppedBytes += dropped.Length;
            }

            if (_drainScheduled)
            {
                return false;
            }

            _drainScheduled = true;
            return true;
        }
    }

    /// <summary>
    /// Забирает накопленное; следующий <see cref="Append"/> снова попросит
    /// запланировать разгребание.
    /// </summary>
    public IReadOnlyList<byte[]> Drain()
    {
        lock (_gate)
        {
            var batch = _chunks.ToArray();
            Reset();
            return batch;
        }
    }

    /// <summary>
    /// Выбрасывает накопленное, не разбирая: вывод мёртвого процесса не должен
    /// попасть в терминал, перезапущенный в том же тайле.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            Reset();
        }
    }

    private void Reset()
    {
        _chunks.Clear();
        _pendingBytes = 0;
        _drainScheduled = false;
    }
}
