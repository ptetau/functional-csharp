using System.Diagnostics;
using RayTracer.Core;

namespace RayTracer.Tests;

/// <summary>The worked examples in specs/ray-tracer.md, one test each.</summary>
public class SpecExampleTests
{
    [Fact]
    public void R11_background_only_scene_renders_every_pixel_in_the_background_colour()
    {
        var scene = Scenes.Parse("""
            image 2 2 1
            camera 0 0 -1 0 0 0 90
            background 1 0 0
            """);

        var ppm = Renderer.Render(scene);

        var header = "P6\n2 2\n255\n"u8.ToArray();
        Assert.Equal(header, ppm.Take(header.Length));
        Assert.Equal(Enumerable.Repeat(new byte[] { 255, 0, 0 }, 4).SelectMany(p => p), ppm.Skip(header.Length));
    }

    [Fact]
    public void R4_ray_towards_unit_sphere_hits_at_distance_4_with_normal_facing_back()
    {
        var sphere = new Sphere(Vec3.Zero, 1, Scenes.Diffuse(1));
        var ray = new Ray(new Vec3(0, 0, -5), new Vec3(0, 0, 1));

        var hit = Intersection.Sphere(sphere, ray);

        Assert.NotNull(hit);
        Assert.Equal(4, hit.Value.Distance, 12);
        Assert.Equal(new Vec3(0, 0, -1), hit.Value.Normal);
        Assert.Equal(new Vec3(0, 0, -1), hit.Value.Point);
    }

    [Fact]
    public void R5_head_on_white_light_on_half_grey_diffuse_gives_0_55()
    {
        var floor = new Plane(new Vec3(0, 1, 0), 0, Scenes.Diffuse(0.5));
        var scene = Scenes.Empty(Colour.Black) with { Lights = [Scenes.WhiteLight], Planes = [floor] };
        var straightDown = new Ray(new Vec3(0, 5, 0), new Vec3(0, -1, 0));

        var colour = Tracer.Trace(scene, straightDown, 0);

        Assert.Equal(0.55, colour.R, 12);
        Assert.Equal(0.55, colour.G, 12);
        Assert.Equal(0.55, colour.B, 12);
    }

    [Fact]
    public void R9_gamma_corrects_and_rounds()
    {
        Assert.Equal(194, Renderer.ToByte(0.55));
        Assert.Equal(194, (int)Math.Round(255 * Math.Pow(0.55, 1 / 2.2)));
    }

    [Fact]
    public void R5_R9_head_on_example_renders_as_194()
    {
        var scene = Scenes.Parse("""
            image 1 1 1
            camera 0 5 0   0 0 0   10
            light 0 10 0   1 1 1
            material grey diffuse 0.5 0.5 0.5
            plane 0 1 0 0 grey
            """);

        var ppm = Renderer.Render(scene);

        Assert.Equal(new byte[] { 194, 194, 194 }, ppm.Skip(ppm.Length - 3));
    }

    [Fact]
    public void Sample_scene_renders_in_under_10_seconds()
    {
        var scene = Scenes.Parse(Scenes.Sample);
        var clock = Stopwatch.StartNew();

        var ppm = Renderer.Render(scene);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
        Assert.Equal("P6\n320 240\n255\n".Length + 320 * 240 * 3, ppm.Length);
    }
}
