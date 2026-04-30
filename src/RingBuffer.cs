namespace Terminal;

/// <summary>
/// Fixed-capacity circular byte buffer. Thread-unsafe — caller synchronises.
/// </summary>
public sealed class RingBuffer
{
    private readonly byte[] buf;
    private int writeIndex;       // next slot to write (0..capacity-1)
    private int filled;           // bytes currently stored (0..capacity)

    public int Capacity => buf.Length;
    public int Count => filled;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        buf = new byte[capacity];
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;

        // If incoming chunk is larger than capacity, keep only its tail.
        if (data.Length >= buf.Length)
        {
            data[^buf.Length..].CopyTo(buf);
            writeIndex = 0;
            filled = buf.Length;
            return;
        }

        var firstSlice = Math.Min(data.Length, buf.Length - writeIndex);
        data[..firstSlice].CopyTo(buf.AsSpan(writeIndex));
        var remaining = data.Length - firstSlice;
        if (remaining > 0)
            data[firstSlice..].CopyTo(buf.AsSpan(0));

        writeIndex = (writeIndex + data.Length) % buf.Length;
        filled = Math.Min(filled + data.Length, buf.Length);
    }

    public byte[] Snapshot()
    {
        var result = new byte[filled];
        if (filled == 0) return result;

        if (filled < buf.Length)
        {
            // Buffer not yet wrapped — bytes are in [0..filled).
            buf.AsSpan(0, filled).CopyTo(result);
        }
        else
        {
            // Wrapped — oldest byte is at writeIndex.
            var tail = buf.Length - writeIndex;
            buf.AsSpan(writeIndex, tail).CopyTo(result.AsSpan(0));
            buf.AsSpan(0, writeIndex).CopyTo(result.AsSpan(tail));
        }
        return result;
    }
}
