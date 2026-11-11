namespace RayTracer.Core;

/// <summary>Where a ray meets a surface. Normal is a unit vector facing back towards the ray.</summary>
public readonly record struct Hit(double Distance, Vec3 Point, Vec3 Normal, Material Material);

public static class Intersection
{
    /// <summary>How far along the surface normal secondary rays start, so a surface can't hit itself (R-8).</summary>
    public const double SurfaceOffset = 1e-4;

    // Hot path. The obvious version is
    //     scene.Spheres.Select(s => Sphere(s, ray)).Concat(scene.Planes.Select(p => Plane(p, ray))).Aggregate((Hit?)null, Closer)
    // but its closures and enumerators cost ~536 bytes and ~600 ns per call against ~30 ns of maths.
    // ImmutableArray's own Aggregate has no enumerator, and a static lambda carrying the ray in the
    // accumulator allocates nothing.
    // The scene's lists are ValueList (for value equality), so go through .Items: on the ValueList itself,
    // Aggregate binds to Enumerable.Aggregate, which boxes the struct and allocates an enumerator per call
    // (160 B per Nearest, 92 MB per sample render, ~30% slower).

    /// <summary>The nearest hit in front of the ray's origin, or null if it hits nothing (R-4).</summary>
    public static Hit? Nearest(Scene scene, Ray ray) =>
        scene.Planes.Items.Aggregate(
            scene.Spheres.Items.Aggregate(
                (Best: (Hit?)null, Ray: ray),
                static (acc, sphere) => (Closer(acc.Best, Sphere(sphere, acc.Ray)), acc.Ray)),
            static (acc, plane) => (Closer(acc.Best, Plane(plane, acc.Ray)), acc.Ray))
        .Best;

    /// <summary>Whether anything lies between a point and a light (R-6).</summary>
    public static bool Blocked(Scene scene, Ray towardsLight, double lightDistance) =>
        scene.Planes.Items.Aggregate(
            scene.Spheres.Items.Aggregate(
                (Blocked: false, Ray: towardsLight, Distance: lightDistance),
                static (acc, sphere) => (acc.Blocked || Sphere(sphere, acc.Ray) is { } hit && hit.Distance < acc.Distance, acc.Ray, acc.Distance)),
            static (acc, plane) => (acc.Blocked || Plane(plane, acc.Ray) is { } hit && hit.Distance < acc.Distance, acc.Ray, acc.Distance))
        .Blocked;

    public static Hit? Sphere(Sphere sphere, Ray ray)
    {
        // |o + t d - c|² = r², with |d| = 1:  t² + 2bt + c = 0
        var toOrigin = ray.Origin - sphere.Centre;
        var b = toOrigin.Dot(ray.Direction);
        var c = toOrigin.Dot(toOrigin) - sphere.Radius * sphere.Radius;
        var discriminant = b * b - c;
        var root = discriminant >= 0 ? Math.Sqrt(discriminant) : double.NaN;
        var near = -b - root;
        var far = -b + root;
        var distance = near > 0 ? near : far > 0 ? far : double.NaN;
        return double.IsNaN(distance)
            ? null
            : At(ray, distance, (ray.At(distance) - sphere.Centre) * (1 / sphere.Radius), sphere.Material);
    }

    public static Hit? Plane(Plane plane, Ray ray)
    {
        var facing = plane.Normal.Dot(ray.Direction);
        var distance = (plane.Distance - plane.Normal.Dot(ray.Origin)) / facing;
        return Math.Abs(facing) < 1e-12 || !(distance > 0)
            ? null
            : At(ray, distance, plane.Normal, plane.Material);
    }

    // Both sides of a surface are lit, so turn the normal to face the incoming ray.
    private static Hit At(Ray ray, double distance, Vec3 normal, Material material) =>
        new(distance, ray.At(distance), normal.Dot(ray.Direction) > 0 ? -normal : normal, material);

    // Ties keep the earlier shape, so the result never depends on evaluation order.
    private static Hit? Closer(Hit? best, Hit? next) =>
        next is { } candidate && (best is not { } current || candidate.Distance < current.Distance) ? next : best;
}
