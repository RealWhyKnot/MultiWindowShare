namespace MultiWindowShare.Core;

public readonly record struct Tile(int X, int Y, int Width, int Height);

public readonly record struct SourceSize(int Width, int Height);

public static class GridLayout
{
    // Minimized windows report no size until their first frame; a 16:9 stand-in keeps their slot.
    private const double FallbackAspect = 16 / 9.0;

    // Packs one final letterboxed rect per source, index-aligned with the input and never reordered.
    // Every arrangement in a small candidate set is scored by total displayed area and the best one
    // wins, so utilization can only match or beat a plain square grid. Aspect is always preserved.
    public static IReadOnlyList<Tile> Compute(IReadOnlyList<SourceSize> sources, int canvasWidth, int canvasHeight)
    {
        int n = sources.Count;
        if (n == 0 || canvasWidth <= 0 || canvasHeight <= 0)
        {
            return [];
        }

        var aspects = new double[n];
        for (int i = 0; i < n; i++)
        {
            SourceSize s = sources[i];
            aspects[i] = s.Width > 0 && s.Height > 0 ? s.Width / (double)s.Height : FallbackAspect;
        }

        Tile[]? best = null;
        long bestScore = -1;

        // Justified candidates go first and ties keep the earlier candidate, so equal-area
        // arrangements prefer justified over uniform and fewer rows over more (two matching
        // windows land side by side, not stacked).
        for (int rows = 1; rows <= n; rows++)
        {
            Consider(JustifiedRows(aspects, rows, canvasWidth, canvasHeight), ref best, ref bestScore);
        }

        for (int cols = n; cols >= 1; cols--)
        {
            Consider(UniformGrid(aspects, cols, canvasWidth, canvasHeight), ref best, ref bestScore);
        }

        return best!;
    }

    private static void Consider(Tile[] candidate, ref Tile[]? best, ref long bestScore)
    {
        long score = 0;
        foreach (Tile t in candidate)
        {
            score += (long)t.Width * t.Height;
        }

        if (score > bestScore)
        {
            best = candidate;
            bestScore = score;
        }
    }

    // Every source in a row shares the row's height and gets width proportional to its aspect, so a
    // row has no internal bars; row heights grow to spend the whole canvas height where they can.
    private static Tile[] JustifiedRows(double[] aspects, int rowCount, int canvasWidth, int canvasHeight)
    {
        int n = aspects.Length;
        int[] counts = PartitionByAspect(aspects, rowCount);

        var rowAspect = new double[rowCount];
        int index = 0;
        for (int j = 0; j < rowCount; j++)
        {
            for (int k = 0; k < counts[j]; k++)
            {
                rowAspect[j] += aspects[index++];
            }
        }

        // Start every row at an equal share, then waterfill leftover height into rows that can
        // still widen (a row is capped once it spans the full canvas width).
        var caps = new double[rowCount];
        var heights = new double[rowCount];
        double used = 0;
        for (int j = 0; j < rowCount; j++)
        {
            caps[j] = canvasWidth / rowAspect[j];
            heights[j] = Math.Min(canvasHeight / (double)rowCount, caps[j]);
            used += heights[j];
        }

        for (int pass = 0; pass < rowCount; pass++)
        {
            double leftover = canvasHeight - used;
            int uncapped = 0;
            for (int j = 0; j < rowCount; j++)
            {
                if (heights[j] < caps[j] - 0.01)
                {
                    uncapped++;
                }
            }

            if (leftover < 0.01 || uncapped == 0)
            {
                break;
            }

            double add = leftover / uncapped;
            for (int j = 0; j < rowCount; j++)
            {
                if (heights[j] < caps[j] - 0.01)
                {
                    double grown = Math.Min(heights[j] + add, caps[j]);
                    used += grown - heights[j];
                    heights[j] = grown;
                }
            }
        }

        // Cumulative rounding keeps neighbours flush: each edge is rounded once and shared.
        var tiles = new Tile[n];
        double y = (canvasHeight - used) / 2;
        index = 0;
        for (int j = 0; j < rowCount; j++)
        {
            int top = (int)Math.Round(y);
            int tileHeight = (int)Math.Round(y + heights[j]) - top;
            double x = (canvasWidth - (rowAspect[j] * heights[j])) / 2;
            for (int k = 0; k < counts[j]; k++, index++)
            {
                int left = (int)Math.Round(x);
                x += aspects[index] * heights[j];
                tiles[index] = new Tile(left, top, (int)Math.Round(x) - left, tileHeight);
            }

            y += heights[j];
        }

        return tiles;
    }

    // Contiguous split into rowCount groups of roughly equal aspect sum, so rows come out with
    // similar widths. Order is never changed.
    private static int[] PartitionByAspect(double[] aspects, int rowCount)
    {
        double total = 0;
        foreach (double a in aspects)
        {
            total += a;
        }

        double target = total / rowCount;
        var counts = new int[rowCount];
        int row = 0;
        double sum = 0;
        for (int i = 0; i < aspects.Length; i++)
        {
            counts[row]++;
            sum += aspects[i];
            int sourcesLeft = aspects.Length - i - 1;
            int rowsLeft = rowCount - row - 1;
            if (rowsLeft > 0 && sourcesLeft >= rowsLeft && (sum >= target || sourcesLeft == rowsLeft))
            {
                row++;
                sum = 0;
            }
        }

        return counts;
    }

    // Equal cells with per-cell letterboxing; beats justified rows when very different aspects
    // would otherwise share one row height. A partial last row is centred.
    private static Tile[] UniformGrid(double[] aspects, int cols, int canvasWidth, int canvasHeight)
    {
        int n = aspects.Length;
        int rows = (n + cols - 1) / cols;
        double cellWidth = canvasWidth / (double)cols;
        double cellHeight = canvasHeight / (double)rows;

        var tiles = new Tile[n];
        for (int i = 0; i < n; i++)
        {
            int col = i % cols;
            int row = i / cols;
            int inRow = Math.Min(cols, n - (row * cols));
            double rowLeft = (canvasWidth - (inRow * cellWidth)) / 2;
            double w = Math.Min(cellWidth, cellHeight * aspects[i]);
            double h = w / aspects[i];
            double x = rowLeft + (col * cellWidth) + ((cellWidth - w) / 2);
            double y = (row * cellHeight) + ((cellHeight - h) / 2);
            int left = (int)Math.Round(x);
            int top = (int)Math.Round(y);
            tiles[i] = new Tile(left, top, (int)Math.Round(x + w) - left, (int)Math.Round(y + h) - top);
        }

        return tiles;
    }
}
