using RayTracer.Core;

namespace RayTracer.Tests;

public class ShadingTests
{
    private static readonly Ray StraightDown = new(new Vec3(0, 5, 0), new Vec3(0, -1, 0));

    private static Scene Floor(Material material, params Light[] lights) =>
        Scenes.Empty(new Colour(0.2, 0.4, 0.6)) with
        {
            Lights = [.. lights],
            Planes = [new Plane(new Vec3(0, 1, 0), 0, material)],
        };

    [Fact]
    public void R4_ray_that_hits_nothing_is_the_background_colour()
    {
        var scene = Floor(Scenes.Diffuse(0.5), Scenes.WhiteLight);

        Assert.Equal(new Colour(0.2, 0.4, 0.6), Tracer.Trace(scene, new Ray(Vec3.Zero, new Vec3(0, 1, 0)), 0));
    }

    [Fact]
    public void R5_light_at_an_angle_scales_by_the_cosine()
    {
        var light = new Light(new Vec3(10, 10, 0), new Colour(1, 1, 1));

        var colour = Tracer.Trace(Floor(Scenes.Diffuse(0.5), light), StraightDown, 0);

        // Not exact: the shadow ray starts 1e-4 above the floor (R-8), which tilts the light direction slightly.
        Assert.Equal(0.5 * Math.Sqrt(0.5) + 0.05, colour.R, 5);
    }

    [Fact]
    public void R5_light_colour_multiplies_material_colour_and_lights_add_up()
    {
        var material = new Material("m", new Colour(0.5, 1, 0.25), 0);
        var red = new Light(new Vec3(0, 10, 0), new Colour(1, 0, 0));
        var green = new Light(new Vec3(0, 20, 0), new Colour(0, 0.5, 0));

        var colour = Tracer.Trace(Floor(material, red, green), StraightDown, 0);

        Assert.Equal(0.5 + 0.05, colour.R, 12);
        Assert.Equal(0.5 + 0.1, colour.G, 12);
        Assert.Equal(0.025, colour.B, 12);
    }

    [Fact]
    public void R5_light_behind_the_surface_contributes_nothing()
    {
        var below = new Light(new Vec3(0, -10, 0), new Colour(1, 1, 1));

        var colour = Tracer.Trace(Floor(Scenes.Diffuse(0.5), below), StraightDown, 0);

        Assert.Equal(0.05, colour.R, 12);
    }

    [Fact]
    public void R6_surface_between_point_and_light_casts_a_shadow()
    {
        var scene = Floor(Scenes.Diffuse(0.5), Scenes.WhiteLight) with
        {
            Spheres = [new Sphere(new Vec3(0, 7, 0), 1, Scenes.Diffuse(1))],
        };
        var fromTheSide = new Ray(new Vec3(-5, 0.000001 + 5, 0), new Vec3(1, -1, 0).Normalised());

        var colour = Tracer.Trace(scene, fromTheSide, 0);

        Assert.Equal(0.05, colour.R, 12);
    }

    [Fact]
    public void R6_surface_beyond_the_light_casts_no_shadow()
    {
        var scene = Floor(Scenes.Diffuse(0.5), Scenes.WhiteLight) with
        {
            Spheres = [new Sphere(new Vec3(0, 20, 0), 1, Scenes.Diffuse(1))],
        };

        Assert.Equal(0.55, Tracer.Trace(scene, new Ray(new Vec3(-5, 5, 0), new Vec3(1, -1, 0).Normalised()), 0).R, 9);
    }

    [Fact]
    public void R7_reflective_surface_mixes_diffuse_and_reflected_colour()
    {
        // Looking straight down at a mirror floor: the reflection goes straight up into the background.
        var scene = Floor(Scenes.Mirror(0.5, 0.8), Scenes.WhiteLight);

        var colour = Tracer.Trace(scene, StraightDown, 0);

        Assert.Equal(0.2 * 0.55 + 0.8 * 0.2, colour.R, 12);
        Assert.Equal(0.2 * 0.55 + 0.8 * 0.4, colour.G, 12);
        Assert.Equal(0.2 * 0.55 + 0.8 * 0.6, colour.B, 12);
    }

    [Fact]
    public void R7_a_ray_at_the_bounce_limit_returns_the_background_without_hitting_anything()
    {
        var scene = Floor(Scenes.Diffuse(0.5), Scenes.WhiteLight);

        Assert.Equal(scene.Background, Tracer.Trace(scene, StraightDown, Tracer.MaxBounces));
        Assert.Equal(new Colour(0.55, 0.55, 0.55), Tracer.Trace(scene, StraightDown, Tracer.MaxBounces - 1));
    }

    [Fact]
    public void R7_two_facing_perfect_mirrors_stop_after_5_bounces_with_the_background()
    {
        // Between two perfect mirrors the ray bounces forever; the 5th bounce gives the background.
        var mirror = Scenes.Mirror(1, 1);
        var scene = Scenes.Empty(new Colour(0.3, 0.6, 0.9)) with
        {
            Lights = [Scenes.WhiteLight],
            Planes = [new Plane(new Vec3(0, 1, 0), 0, mirror), new Plane(new Vec3(0, 1, 0), 2, mirror)],
        };

        Assert.Equal(scene.Background, Tracer.Trace(scene, new Ray(new Vec3(0, 1, 0), new Vec3(0, -1, 0)), 0));
    }

    [Fact]
    public void R7_each_bounce_counts_once()
    {
        // Half-reflective mirrors facing each other: after k bounces the background is weighted 0.5^k.
        var mirror = new Material("half", Colour.Black, 0.5);
        var scene = Scenes.Empty(new Colour(1, 1, 1)) with
        {
            Planes = [new Plane(new Vec3(0, 1, 0), 0, mirror), new Plane(new Vec3(0, 1, 0), 2, mirror)],
        };

        var colour = Tracer.Trace(scene, new Ray(new Vec3(0, 1, 0), new Vec3(0, -1, 0)), 0);

        Assert.Equal(Math.Pow(0.5, 5), colour.R, 12);
    }

    [Fact]
    public void R8_lit_sphere_does_not_shadow_itself()
    {
        var scene = Scenes.Empty(Colour.Black) with
        {
            Lights = [new Light(new Vec3(0, 0, -10), new Colour(1, 1, 1))],
            Spheres = [new Sphere(Vec3.Zero, 1, Scenes.Diffuse(0.5))],
        };

        var colour = Tracer.Trace(scene, new Ray(new Vec3(0, 0, -5), new Vec3(0, 0, 1)), 0);

        Assert.Equal(0.55, colour.R, 12);
    }

    [Fact]
    public void R8_mirror_does_not_reflect_itself()
    {
        var scene = Scenes.Empty(new Colour(0, 0, 1)) with
        {
            Spheres = [new Sphere(Vec3.Zero, 1, Scenes.Mirror(0, 1))],
        };

        var colour = Tracer.Trace(scene, new Ray(new Vec3(0, 0, -5), new Vec3(0, 0, 1)), 0);

        Assert.Equal(new Colour(0, 0, 1), colour);
    }
}
