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
    public void ProducesOneTilePerSource(int sources)
    {
        Assert.Equal(sources, GridLayout.Compute(sources, 1920, 1080).Count);
    }

    [Fact]
    public void SplitsFourSourcesIntoATwoByTwo()
    {
        IReadOnlyList<Tile> tiles = GridLayout.Compute(4, 1920, 1080);

        Assert.All(tiles, t => Assert.Equal((960, 540), (t.Width, t.Height)));
        Assert.Equal(new Tile(0, 0, 960, 540), tiles[0]);
        Assert.Equal(new Tile(960, 540, 960, 540), tiles[3]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(12)]
    public void KeepsEveryTileInsideTheCanvas(int sources)
    {
        const int width = 1600;
        const int height = 900;

        foreach (Tile t in GridLayout.Compute(sources, width, height))
        {
            Assert.InRange(t.X, 0, width - t.Width);
            Assert.InRange(t.Y, 0, height - t.Height);
        }
    }

    [Fact]
    public void TilesDoNotOverlap()
    {
        IReadOnlyList<Tile> tiles = GridLayout.Compute(6, 1200, 800);

        for (int i = 0; i < tiles.Count; i++)
        {
            for (int j = i + 1; j < tiles.Count; j++)
            {
                Assert.False(Overlaps(tiles[i], tiles[j]), $"tiles {i} and {j} overlap");
            }
        }
    }

    [Fact]
    public void FitPreservesTheSourceAspectRatio()
    {
        Tile fit = GridLayout.Fit(new Tile(0, 0, 800, 800), 1920, 1080);

        Assert.Equal(1920f / 1080f, fit.Width / (float)fit.Height, 0.01f);
    }

    [Fact]
    public void FitCentresInsideTheTile()
    {
        Tile fit = GridLayout.Fit(new Tile(0, 0, 800, 800), 1920, 1080);

        int topGap = fit.Y;
        int bottomGap = 800 - (fit.Y + fit.Height);
        Assert.True(Math.Abs(topGap - bottomGap) <= 1, $"letterbox bars differ: {topGap} vs {bottomGap}");
        Assert.Equal(0, fit.X);
        Assert.Equal(800, fit.Width);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(100, 4000)]
    [InlineData(4000, 100)]
    [InlineData(400, 300)]
    [InlineData(1, 1)]
    public void FitNeverEscapesTheTile(int sourceWidth, int sourceHeight)
    {
        var tile = new Tile(100, 50, 400, 300);

        Tile fit = GridLayout.Fit(tile, sourceWidth, sourceHeight);

        Assert.InRange(fit.X, tile.X, tile.X + tile.Width - fit.Width);
        Assert.InRange(fit.Y, tile.Y, tile.Y + tile.Height - fit.Height);
    }

    [Fact]
    public void FitToleratesAZeroSizedSource()
    {
        var tile = new Tile(0, 0, 320, 240);

        Assert.Equal(tile, GridLayout.Fit(tile, 0, 0));
    }

    private static bool Overlaps(Tile a, Tile b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width &&
        a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
