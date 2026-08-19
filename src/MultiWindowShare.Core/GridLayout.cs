namespace MultiWindowShare.Core;

public readonly record struct Tile(int X, int Y, int Width, int Height);

public static class GridLayout
{
    // Pack n tiles into a canvasWidth x canvasHeight surface as a near-square grid, row-major. Tiles
    // are uniform; per-source aspect handling (letterboxing) is a draw-time concern, not this math.
    // Integer division can leave a few unused pixels at the right and bottom edges.
    public static IReadOnlyList<Tile> Compute(int n, int canvasWidth, int canvasHeight)
    {
        if (n <= 0)
        {
            return [];
        }

        int cols = (int)Math.Ceiling(Math.Sqrt(n));
        int rows = (int)Math.Ceiling(n / (double)cols);
        int tileWidth = canvasWidth / cols;
        int tileHeight = canvasHeight / rows;

        var tiles = new List<Tile>(n);
        for (int i = 0; i < n; i++)
        {
            int col = i % cols;
            int row = i / cols;
            tiles.Add(new Tile(col * tileWidth, row * tileHeight, tileWidth, tileHeight));
        }

        return tiles;
    }

    // Largest centred rect inside tile that keeps the source's aspect ratio, so a captured window is
    // letterboxed rather than stretched. Returns the tile unchanged for a degenerate source size.
    public static Tile Fit(Tile tile, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return tile;
        }

        double scale = Math.Min(tile.Width / (double)sourceWidth, tile.Height / (double)sourceHeight);
        int width = Math.Max(1, (int)(sourceWidth * scale));
        int height = Math.Max(1, (int)(sourceHeight * scale));
        return new Tile(tile.X + ((tile.Width - width) / 2), tile.Y + ((tile.Height - height) / 2), width, height);
    }
}
