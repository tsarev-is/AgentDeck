using System.Text;
using AgentDeck.Terminal;
using NUnit.Framework;

namespace AgentDeck.Tests.Terminal;

/// <summary>
/// Накопитель вывода PTY: схлопывание пачки в одно разгребание и потолок памяти.
/// </summary>
[TestFixture]
public class OutputBufferTests
{
    /// <summary>
    /// Разгребание планирует только первый чанк — иначе очередь диспетчера
    /// росла бы по задаче на каждые прочитанные байты.
    /// </summary>
    [Test]
    public void Append_SchedulesDrainOnlyOnceUntilDrained()
    {
        var buffer = new OutputBuffer();

        Assert.Multiple(() =>
        {
            Assert.That(buffer.Append(Bytes("a")), Is.True, "первый чанк планирует разгребание");
            Assert.That(buffer.Append(Bytes("b")), Is.False);
            Assert.That(buffer.Append(Bytes("c")), Is.False);
        });

        var batch = buffer.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(Text(batch), Is.EqualTo("abc"), "порядок вывода должен сохраниться");
            Assert.That(buffer.PendingBytes, Is.Zero);
            Assert.That(buffer.Append(Bytes("d")), Is.True, "после разгребания планируем снова");
        });
    }

    /// <summary>
    /// Пустое разгребание безопасно: задача может добежать после Clear.
    /// </summary>
    [Test]
    public void Drain_WithoutData_ReturnsEmpty()
    {
        Assert.That(new OutputBuffer().Drain(), Is.Empty);
    }

    /// <summary>
    /// Сверх потолка выбрасывается самое старое, хвост остаётся: на экране
    /// нужен конец вывода, а не его начало.
    /// </summary>
    [Test]
    public void Append_OverCapacity_DropsOldestAndKeepsTail()
    {
        var buffer = new OutputBuffer(capacityBytes: 10);

        buffer.Append(Bytes("11111"));
        buffer.Append(Bytes("22222"));
        buffer.Append(Bytes("33333"));

        Assert.Multiple(() =>
        {
            Assert.That(Text(buffer.Drain()), Is.EqualTo("2222233333"));
            Assert.That(buffer.DroppedBytes, Is.EqualTo(5));
        });
    }

    /// <summary>
    /// Чанк крупнее потолка не выбрасывается: терять последний вывод нельзя.
    /// </summary>
    [Test]
    public void Append_SingleOversizedChunk_IsKept()
    {
        var buffer = new OutputBuffer(capacityBytes: 4);

        buffer.Append(Bytes("0123456789"));

        Assert.Multiple(() =>
        {
            Assert.That(Text(buffer.Drain()), Is.EqualTo("0123456789"));
            Assert.That(buffer.DroppedBytes, Is.Zero);
        });
    }

    /// <summary>
    /// Clear выбрасывает вывод мёртвого процесса перед перезапуском тайла.
    /// </summary>
    [Test]
    public void Clear_DropsPendingOutput()
    {
        var buffer = new OutputBuffer();
        buffer.Append(Bytes("stale"));

        buffer.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(buffer.Drain(), Is.Empty);
            Assert.That(buffer.PendingBytes, Is.Zero);
            Assert.That(buffer.Append(Bytes("fresh")), Is.True);
        });
    }

    /// <summary>
    /// Поток чтения PTY пишет, UI-поток разгребает: ни байта не теряется мимо
    /// потолка и ни одна задача разгребания не пропадает.
    /// </summary>
    [Test]
    public void Append_AndDrainConcurrently_LosesNothing()
    {
        const int chunks = 20_000;

        var buffer = new OutputBuffer();
        var drained = 0L;
        var scheduled = 0;
        var producing = true;

        var consumer = Task.Run(() =>
        {
            while (Volatile.Read(ref producing) || buffer.PendingBytes > 0)
            {
                foreach (var chunk in buffer.Drain())
                {
                    Interlocked.Add(ref drained, chunk.Length);
                }
            }
        });

        for (var i = 0; i < chunks; i++)
        {
            if (buffer.Append([1, 2, 3, 4]))
            {
                scheduled++;
            }
        }

        Volatile.Write(ref producing, false);
        Assert.That(consumer.Wait(TimeSpan.FromSeconds(10)), Is.True, "разгребание должно завершиться");

        foreach (var chunk in buffer.Drain())
        {
            drained += chunk.Length;
        }

        Assert.Multiple(() =>
        {
            Assert.That(drained, Is.EqualTo(chunks * 4L), "весь вывод должен дойти до терминала");
            Assert.That(buffer.DroppedBytes, Is.Zero, "потолок в этом объёме не задевается");
            Assert.That(scheduled, Is.GreaterThan(0));
        });
    }

    private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

    private static string Text(IReadOnlyList<byte[]> batch)
        => string.Concat(batch.Select(chunk => Encoding.ASCII.GetString(chunk)));
}
