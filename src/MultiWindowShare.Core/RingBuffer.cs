namespace MultiWindowShare.Core;

// Float ring for the capture -> mix hop. On overflow it drops the oldest samples, since a
// real-time audio path prefers a short glitch over unbounded latency. One producer, one consumer;
// the live path serializes access.
public sealed class RingBuffer
{
    private readonly float[] _buf;
    private int _read;
    private int _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _buf = new float[capacity];
    }

    public int Capacity => _buf.Length;

    public int Count => _count;

    public void Write(ReadOnlySpan<float> src)
    {
        foreach (float s in src)
        {
            _buf[(_read + _count) % _buf.Length] = s;
            if (_count == _buf.Length)
            {
                _read = (_read + 1) % _buf.Length;
            }
            else
            {
                _count++;
            }
        }
    }

    public int Read(Span<float> dest)
    {
        int n = Math.Min(dest.Length, _count);
        for (int i = 0; i < n; i++)
        {
            dest[i] = _buf[(_read + i) % _buf.Length];
        }

        _read = (_read + n) % _buf.Length;
        _count -= n;
        return n;
    }
}
