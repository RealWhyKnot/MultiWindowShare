using MultiWindowShare.Core;
using Xunit;

namespace MultiWindowShare.Tests;

public class GridLayoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(16)]
    public void ProducesOneTilePerSource(int count)
    {
        IReadOnlyList<SourceSize> sources = [.. Enumerable.Repeat(new SourceSize(1920, 1080), count)];

        Assert.Equal(count, GridLayout.Compute(sources, 1920, 1080).Count);
    }

    [Fact]
    public void EveryTileKeepsItsSourceAspectRatio()
    {
        IReadOnlyList<SourceSize> sources =
            [new(1920, 1080), new(1080, 1920), new(1440, 1080), new(800, 600), new(2560, 1080)];

        IReadOnlyList<Tile> tiles = GridLayout.Compute(sources, 1920, 1080);

        for (int i = 0; i < sources.Count; i++)
        {
            double aspect = sources[i].Width / (double)sources[i].Height;
            double widthAtAspect = tiles[i].Height * aspect;
            Assert.True(Math.Abs(tiles[i].Width - widthAtAspect) <= 2,
                $"tile {i} is {tiles[i].Width}x{tiles[i].Height}, expected width near {widthAtAspect:0.#}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(12)]
    public void TilesStayInsideTheCanvas(int count)
    {
        const int width = 1600;
        const int height = 900;
        IReadOnlyList<SourceSize> sources = [.. Enumerable.Range(0, count)
            .Select(i => i % 3 == 0 ? new SourceSize(1080, 1920) : new SourceSize(1920, 1080))];

        foreach (Tile t in GridLayout.Compute(sources, width, height))
        {
            Assert.InRange(t.X, 0, width - t.Width);
            Assert.InRange(t.Y, 0, height - t.Height);
        }
    }

    [Fact]
    public void TilesDoNotOverlap()
    {
        IReadOnlyList<SourceSize> sources =
            [new(1920, 1080), new(1080, 1920), new(800, 600), new(1920, 1080), new(2560, 1080), new(1024, 768)];

        IReadOnlyList<Tile> tiles = GridLayout.Compute(sources, 1200, 800);

        for (int i = 0; i < tiles.Count; i++)
        {
            for (int j = i + 1; j < tiles.Count; j++)
            {
                Assert.False(Overlaps(tiles[i], tiles[j]), $"tiles {i} and {j} overlap");
            }
        }
    }

    [Fact]
    public void TwoWidescreenSourcesSplitTheCanvasSideBySide()
    {
        IReadOnlyList<SourceSize> sources = [new(1920, 1080), new(1920, 1080)];

        IReadOnlyList<Tile> tiles = GridLayout.Compute(sources, 1920, 1080);

        Assert.Equal(new Tile(0, 270, 960, 540), tiles[0]);
        Assert.Equal(new Tile(960, 270, 960, 540), tiles[1]);
    }

    [Theory]
    [InlineData(new[] { 1920, 1080, 1920, 1080 })]
    [InlineData(new[] { 1920, 1080, 1920, 1080, 1920, 1080, 1920, 1080, 1920, 1080 })]
    [InlineData(new[] { 1080, 1920, 1920, 1080, 1920, 1080 })]
    [InlineData(new[] { 1440, 1080, 1920, 1080 })]
    [InlineData(new[] { 1920, 1080, 800, 600, 1080, 1920, 2560, 1080, 1024, 768, 1920, 1080 })]
    public void UtilizationNeverDropsBelowTheOldSquareGrid(int[] dims)
    {
        IReadOnlyList<SourceSize> sources = [.. Enumerable.Range(0, dims.Length / 2)
            .Select(i => new SourceSize(dims[i * 2], dims[(i * 2) + 1]))];

        long area = TotalArea(GridLayout.Compute(sources, 1920, 1080));

        Assert.True(area >= OldSquareGridArea(sources, 1920, 1080),
            $"packed {area} px² but the old square grid managed {OldSquareGridArea(sources, 1920, 1080)}");
    }

    [Fact]
    public void FiveWidescreenSourcesUseTwoUnevenRows()
    {
        IReadOnlyList<SourceSize> sources = [.. Enumerable.Repeat(new SourceSize(1920, 1080), 5)];

        long area = TotalArea(GridLayout.Compute(sources, 1920, 1080));

        Assert.True(area >= (long)(1920 * 1080 * 0.83), $"only {area} of {1920 * 1080} px² used");
    }

    [Fact]
    public void SameInputsProduceTheSameTiles()
    {
        IReadOnlyList<SourceSize> sources = [new(1920, 1080), new(1080, 1920), new(800, 600)];

        Assert.Equal(GridLayout.Compute(sources, 1600, 900), GridLayout.Compute(sources, 1600, 900));
    }

    [Fact]
    public void AOnePixelSourceResizeBarelyMovesTheLayout()
    {
        IReadOnlyList<SourceSize> before = [new(1600, 900), new(1600, 900)];
        IReadOnlyList<SourceSize> after = [new(1600, 900), new(1601, 900)];

        IReadOnlyList<Tile> a = GridLayout.Compute(before, 1920, 1080);
        IReadOnlyList<Tile> b = GridLayout.Compute(after, 1920, 1080);

        for (int i = 0; i < a.Count; i++)
        {
            Assert.True(Math.Abs(a[i].X - b[i].X) <= 2 && Math.Abs(a[i].Y - b[i].Y) <= 2
                && Math.Abs(a[i].Width - b[i].Width) <= 2 && Math.Abs(a[i].Height - b[i].Height) <= 2,
                $"tile {i} jumped from {a[i]} to {b[i]}");
        }
    }

    [Fact]
    public void TilesFollowInputOrderRowMajor()
    {
        IReadOnlyList<SourceSize> sources = [.. Enumerable.Repeat(new SourceSize(1920, 1080), 5)];

        IReadOnlyList<Tile> tiles = GridLayout.Compute(sources, 1920, 1080);

        for (int i = 1; i < tiles.Count; i++)
        {
            bool sameRowFurtherRight = tiles[i].Y == tiles[i - 1].Y && tiles[i].X > tiles[i - 1].X;
            bool lowerRow = tiles[i].Y > tiles[i - 1].Y;
            Assert.True(sameRowFurtherRight || lowerRow, $"tile {i} at {tiles[i]} precedes tile {i - 1} at {tiles[i - 1]}");
        }
    }

    [Fact]
    public void AZeroSizedSourceStillGetsATileInBounds()
    {
        IReadOnlyList<SourceSize> sources = [new(1920, 1080), new(0, 0)];

        IReadOnlyList<Tile> tiles = GridLayout.Compute(sources, 1920, 1080);

        Assert.Equal(2, tiles.Count);
        foreach (Tile t in tiles)
        {
            Assert.True(t.Width > 0 && t.Height > 0);
            Assert.InRange(t.X, 0, 1920 - t.Width);
            Assert.InRange(t.Y, 0, 1080 - t.Height);
        }
    }

    [Fact]
    public void ASingleSourceMatchingTheCanvasFillsIt()
    {
        IReadOnlyList<SourceSize> sources = [new(1920, 1080)];

        Assert.Equal(new Tile(0, 0, 1920, 1080), GridLayout.Compute(sources, 1920, 1080)[0]);
    }

    private static long TotalArea(IReadOnlyList<Tile> tiles) =>
        tiles.Sum(t => (long)t.Width * t.Height);

    // The pre-aspect-aware algorithm: ceil-sqrt uniform grid, then letterbox into each cell.
    private static long OldSquareGridArea(IReadOnlyList<SourceSize> sources, int canvasWidth, int canvasHeight)
    {
        int cols = (int)Math.Ceiling(Math.Sqrt(sources.Count));
        int rows = (int)Math.Ceiling(sources.Count / (double)cols);
        int tileWidth = canvasWidth / cols;
        int tileHeight = canvasHeight / rows;

        long area = 0;
        foreach (SourceSize s in sources)
        {
            double scale = Math.Min(tileWidth / (double)s.Width, tileHeight / (double)s.Height);
            area += (long)Math.Max(1, (int)(s.Width * scale)) * Math.Max(1, (int)(s.Height * scale));
        }

        return area;
    }

    private static bool Overlaps(Tile a, Tile b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width &&
        a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
