using RayTracer.Core;

namespace RayTracer.Tests;

public class IntersectionTests
{
    private static readonly Material M = Scenes.Diffuse(1);

    [Fact]
    public void Ray_missing_a_sphere_has_no_hit()
    {
        var hit = Intersection.Sphere(new Sphere(Vec3.Zero, 1, M), new Ray(new Vec3(0, 2, -5), new Vec3(0, 0, 1)));

        Assert.Null(hit);
    }

    [Fact]
    public void Sphere_behind_the_ray_is_not_hit()
    {
        var hit = Intersection.Sphere(new Sphere(Vec3.Zero, 1, M), new Ray(new Vec3(0, 0, 5), new Vec3(0, 0, 1)));

        Assert.Null(hit);
    }

    [Fact]
    public void Ray_from_inside_a_sphere_hits_the_far_side_with_normal_facing_inwards()
    {
        var hit = Intersection.Sphere(new Sphere(Vec3.Zero, 2, M), new Ray(Vec3.Zero, new Vec3(1, 0, 0)));

        Assert.NotNull(hit);
        Assert.Equal(2, hit.Value.Distance, 12);
        Assert.Equal(new Vec3(-1, 0, 0), hit.Value.Normal);
    }

    [Fact]
    public void Ray_grazing_a_sphere_touches_it_once()
    {
        var hit = Intersection.Sphere(new Sphere(Vec3.Zero, 1, M), new Ray(new Vec3(1, 0, -5), new Vec3(0, 0, 1)));

        Assert.NotNull(hit);
        Assert.Equal(5, hit.Value.Distance, 6);
    }

    [Fact]
    public void Ray_down_onto_a_plane_hits_it_with_its_normal()
    {
        var plane = new Plane(new Vec3(0, 1, 0), -1, M);

        var hit = Intersection.Plane(plane, new Ray(new Vec3(3, 4, 0), new Vec3(0, -1, 0)));

        Assert.NotNull(hit);
        Assert.Equal(5, hit.Value.Distance, 12);
        Assert.Equal(new Vec3(3, -1, 0), hit.Value.Point);
        Assert.Equal(new Vec3(0, 1, 0), hit.Value.Normal);
    }

    [Fact]
    public void Ray_up_onto_a_plane_sees_the_normal_turned_towards_it()
    {
        var hit = Intersection.Plane(new Plane(new Vec3(0, 1, 0), 0, M), new Ray(new Vec3(0, -2, 0), new Vec3(0, 1, 0)));

        Assert.NotNull(hit);
        Assert.Equal(new Vec3(0, -1, 0), hit.Value.Normal);
    }

    [Fact]
    public void Ray_parallel_to_a_plane_or_pointing_away_misses_it()
    {
        var plane = new Plane(new Vec3(0, 1, 0), 0, M);

        Assert.Null(Intersection.Plane(plane, new Ray(new Vec3(0, 1, 0), new Vec3(1, 0, 0))));
        Assert.Null(Intersection.Plane(plane, new Ray(new Vec3(0, 1, 0), new Vec3(0, 1, 0))));
    }

    [Fact]
    public void R4_nearest_surface_wins_whatever_the_order_in_the_scene()
    {
        var near = new Sphere(new Vec3(0, 0, 0), 1, Scenes.Diffuse(0.2));
        var far = new Sphere(new Vec3(0, 0, 5), 1, Scenes.Diffuse(0.8));
        var ray = new Ray(new Vec3(0, 0, -5), new Vec3(0, 0, 1));

        var forwards = Intersection.Nearest(Scenes.Empty(Colour.Black) with { Spheres = [near, far] }, ray);
        var backwards = Intersection.Nearest(Scenes.Empty(Colour.Black) with { Spheres = [far, near] }, ray);

        Assert.Equal(4, forwards!.Value.Distance, 12);
        Assert.Equal(forwards, backwards);
    }

    [Fact]
    public void R4_nearest_considers_planes_and_spheres_together()
    {
        var sphere = new Sphere(new Vec3(0, 0, 10), 1, M);
        var wall = new Plane(new Vec3(0, 0, -1), -3, M);

        var hit = Intersection.Nearest(
            Scenes.Empty(Colour.Black) with { Spheres = [sphere], Planes = [wall] },
            new Ray(Vec3.Zero, new Vec3(0, 0, 1)));

        Assert.Equal(3, hit!.Value.Distance, 12);
    }

    [Fact]
    public void Nothing_in_the_scene_means_no_hit()
    {
        Assert.Null(Intersection.Nearest(Scenes.Empty(Colour.Black), new Ray(Vec3.Zero, new Vec3(0, 0, 1))));
    }
}
