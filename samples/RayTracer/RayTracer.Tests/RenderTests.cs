using System.Collections.Immutable;
using RayTracer.Core;

namespace RayTracer.Tests;

public class RenderTests
{
    [Fact]
    public void R3_square_sample_counts_use_a_grid_of_cell_centres()
    {
        Assert.Equal(
            [new Offset(0.25, 0.25), new Offset(0.75, 0.25), new Offset(0.25, 0.75), new Offset(0.75, 0.75)],
            Sampling.Offsets(4).ToArray());
    }

    [Fact]
    public void R3_one_sample_goes_through_the_pixel_centre()
    {
        Assert.Equal([new Offset(0.5, 0.5)], Sampling.Offsets(1).ToArray());
    }

    [Fact]
    public void R3_other_sample_counts_are_spread_along_the_diagonal()
    {
        Assert.Equal([new Offset(0.25, 0.25), new Offset(0.75, 0.75)], Sampling.Offsets(2).ToArray());
        Assert.Equal([1 / 6.0, 0.5, 5 / 6.0], Sampling.Offsets(3).Select(o => o.X));
    }

    [Fact]
    public void R3_pixel_colour_is_the_average_of_its_samples()
    {
        // Left half of the view sees a black wall at x = -0.5, right half the white background.
        // A 1×1 image with 2×2 samples sees two of each.
        var scene = Scenes.Parse("""
            image 1 1 4
            camera 0 0 -1   0 0 0   90
            background 1 1 1
            material black diffuse 0 0 0
            plane 1 0 0   -0.5   black
            """);
        var view = View.Of(scene.Camera, scene.Image);

        var colour = Renderer.Pixel(scene, view, Sampling.Offsets(4), 0, 0);

        Assert.Equal(new Colour(0.5, 0.5, 0.5), colour);
    }

    [Fact]
    public void Camera_looks_at_its_target_with_x_to_the_right_and_y_up()
    {
        var view = View.Of(new Camera(new Vec3(0, 1, -5), new Vec3(0, 1, 0), 90), new ImageSpec(200, 100, 1));

        Assert.Equal(new Vec3(0, 0, 1), view.Through(100, 50).Direction);
        Assert.True(view.Through(199, 50).Direction.X > 0);
        Assert.True(view.Through(100, 0).Direction.Y > 0);
        Assert.Equal(new Vec3(0, 1, 1).Normalised().Y, view.Through(100, 0).Direction.Y, 12);
    }

    [Fact]
    public void R9_clamps_before_gamma()
    {
        Assert.Equal(0, Renderer.ToByte(-0.5));
        Assert.Equal(0, Renderer.ToByte(0));
        Assert.Equal(255, Renderer.ToByte(1));
        Assert.Equal(255, Renderer.ToByte(7));
        Assert.Equal(186, Renderer.ToByte(0.5));
    }

    [Fact]
    public void R10_rendering_twice_gives_identical_bytes()
    {
        var scene = Scenes.Parse(Scenes.Sample) with { Image = new ImageSpec(64, 48, 4) };

        Assert.Equal(Renderer.Render(scene).AsSpan().ToArray(), Renderer.Render(scene).AsSpan().ToArray());
    }

    [Fact]
    public void R12_one_thread_and_four_threads_give_identical_bytes()
    {
        var scene = Scenes.Parse(Scenes.Sample) with { Image = new ImageSpec(80, 61, 3) };

        var single = Renderer.Render(scene);
        var bands = Renderer.Bands(scene.Image.Height, 4);
        var rendered = new ImmutableArray<byte>[bands.Length];
        Parallel.For(0, bands.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 },
            i => rendered[i] = Renderer.RenderRows(scene, bands[i]));
        var parallel = Ppm.Encode(scene.Image, rendered);

        Assert.Equal(single.AsSpan().ToArray(), parallel.AsSpan().ToArray());
    }

    [Theory]
    [InlineData(240, 1)]
    [InlineData(240, 4)]
    [InlineData(61, 4)]
    [InlineData(3, 8)]
    [InlineData(1, 1)]
    public void R12_bands_cover_every_row_once_in_order(int height, int count)
    {
        var bands = Renderer.Bands(height, count);

        Assert.Equal(Enumerable.Range(0, height), bands.SelectMany(b => Enumerable.Range(b.From, b.To - b.From)));
        Assert.All(bands, b => Assert.True(b.To > b.From));
        Assert.True(bands.Length <= count);
    }

    [Fact]
    public void R11_ppm_has_a_P6_header_then_three_bytes_per_pixel()
    {
        var ppm = Ppm.Encode(new ImageSpec(2, 1, 1), [[1, 2, 3], [4, 5, 6]]);

        Assert.Equal("P6\n2 1\n255\n"u8.ToArray().Concat(new byte[] { 1, 2, 3, 4, 5, 6 }), ppm);
    }
}
