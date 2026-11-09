namespace Otoasobi.Probe;

static class ProbePeaks
{
    public static (double[] Lo, double[] Hi) Columns(ReadOnlySpan<float> samples, int columns)
    {
        columns = Math.Clamp(columns, 1, 2048);
        var lo = new double[columns];
        var hi = new double[columns];
        if (samples.Length == 0)
            return (lo, hi);

        var width = samples.Length / (double)columns;
        for (var column = 0; column < columns; column++)
        {
            var start = (int)Math.Floor(column * width);
            var end = (int)Math.Floor((column + 1) * width);
            if (end <= start)
                end = Math.Min(samples.Length, start + 1);
            start = Math.Clamp(start, 0, samples.Length - 1);
            end = Math.Clamp(end, start + 1, samples.Length);
            var min = float.MaxValue;
            var max = float.MinValue;
            for (var index = start; index < end; index++)
            {
                var value = samples[index];
                if (value < min)
                    min = value;
                if (value > max)
                    max = value;
            }

            lo[column] = min;
            hi[column] = max;
        }

        return (lo, hi);
    }
}
