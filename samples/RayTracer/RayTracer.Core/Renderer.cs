using System.Collections.Immutable;
using System.Text;

namespace RayTracer.Core;

/// <summary>A run of image rows, From inclusive, To exclusive.</summary>
public readonly record struct RowRange(int From, int To);

public static class Renderer
{
    public const double Gamma = 2.2;

    /// <summary>The whole scene as a binary PPM file (R-11).</summary>
    public static ImmutableArray<byte> Render(Scene scene) =>
        Ppm.Encode(scene.Image, [RenderRows(scene, new RowRange(0, scene.Image.Height))]);

    /// <summary>
    /// Red, green and blue bytes for each pixel in the rows, left to right, top to bottom.
    /// Rows don't depend on each other, so any split of the image gives the same bytes (R-10, R-12).
    /// </summary>
    public static ImmutableArray<byte> RenderRows(Scene scene, RowRange rows)
    {
        // Colours first, then bytes by index: one array each, instead of a 3-byte array and two
        // enumerators per pixel (SelectMany over ToBytes allocated ~1.2 MB per 320-pixel row).
        var view = View.Of(scene.Camera, scene.Image);
        var offsets = Sampling.Offsets(scene.Image.Samples);
        var width = scene.Image.Width;
        var colours = (
            from y in Enumerable.Range(rows.From, Math.Max(0, rows.To - rows.From))
            from x in Enumerable.Range(0, width)
            select Pixel(scene, view, offsets, x, y)).ToImmutableArray();
        return Enumerable.Range(0, colours.Length * 3)
            .Select(i => ToByte(Channel(colours[i / 3], i % 3)))
            .ToImmutableArray();
    }

    /// <summary>The average colour of the rays fired through one pixel (R-3).</summary>
    public static Colour Pixel(Scene scene, View view, ImmutableArray<Offset> offsets, int x, int y) =>
        offsets.Aggregate(
            (Sum: Colour.Black, Scene: scene, View: view, X: x, Y: y),
            static (acc, o) => (acc.Sum + Tracer.Trace(acc.Scene, acc.View.Through(acc.X + o.X, acc.Y + o.Y), 0), acc.Scene, acc.View, acc.X, acc.Y))
        .Sum * (1.0 / offsets.Length);

    private static double Channel(Colour c, int index) => index switch
    {
        0 => c.R,
        1 => c.G,
        _ => c.B,
    };

    /// <summary>Clamp to 0–1, gamma correct, scale to 0–255 and round (R-9).</summary>
    public static byte ToByte(double channel) =>
        (byte)Math.Round(255 * Math.Pow(Math.Clamp(channel, 0, 1), 1 / Gamma), MidpointRounding.AwayFromZero);

    /// <summary>Splits the image's rows into at most count contiguous, nearly equal bands, in order.</summary>
    public static ImmutableArray<RowRange> Bands(int height, int count)
    {
        var n = Math.Clamp(count, 1, Math.Max(1, height));
        return Enumerable.Range(0, n).Select(i => new RowRange(height * i / n, height * (i + 1) / n)).ToImmutableArray();
    }
}

public static class Ppm
{
    /// <summary>A binary (P6) PPM: the header, then the pixel bytes of each band in order.</summary>
    public static ImmutableArray<byte> Encode(ImageSpec image, IEnumerable<ImmutableArray<byte>> bands) =>
        Encoding.ASCII.GetBytes($"P6\n{image.Width} {image.Height}\n255\n")
            .Concat(bands.SelectMany(band => band))
            .ToImmutableArray();
}
