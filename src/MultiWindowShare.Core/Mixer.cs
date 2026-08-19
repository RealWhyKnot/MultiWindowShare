namespace MultiWindowShare.Core;

public static class Mixer
{
    // Sum equal-length interleaved buffers, each scaled by its gain, into dest. dest and every
    // source span the same number of samples. Output is hard-clamped to [-1, 1]: transparent below
    // 0 dBFS, and a hot sum clips rather than wrapping.
    public static void MixInto(Span<float> dest, IReadOnlyList<float[]> sources, IReadOnlyList<float> gains)
    {
        if (sources.Count != gains.Count)
        {
            throw new ArgumentException("one gain per source is required");
        }

        for (int i = 0; i < dest.Length; i++)
        {
            float sum = 0f;
            for (int s = 0; s < sources.Count; s++)
            {
                sum += sources[s][i] * gains[s];
            }

            dest[i] = sum > 1f ? 1f : sum < -1f ? -1f : sum;
        }
    }
}
