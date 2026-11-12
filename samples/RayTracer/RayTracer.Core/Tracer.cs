namespace RayTracer.Core;

public static class Tracer
{
    /// <summary>A ray at this depth returns the background without looking for a hit (R-7).</summary>
    public const int MaxBounces = 5;

    public const double Ambient = 0.1;

    /// <summary>The colour seen along a ray. Primary rays have depth 0; each reflection adds one.</summary>
    public static Colour Trace(Scene scene, Ray ray, int depth) =>
        depth >= MaxBounces
            ? scene.Background
            : Intersection.Nearest(scene, ray) is { } hit
                ? Shade(scene, ray, hit, depth)
                : scene.Background;

    /// <summary>Diffuse shading mixed with the reflected colour by the material's reflectivity (R-7).</summary>
    public static Colour Shade(Scene scene, Ray ray, Hit hit, int depth) =>
        hit.Material.Reflectivity > 0
            ? Diffuse(scene, hit) * (1 - hit.Material.Reflectivity)
              + Trace(scene, Reflected(ray, hit), depth + 1) * hit.Material.Reflectivity
            : Diffuse(scene, hit);

    /// <summary>Lambert shading summed over the lights that can see the point, plus ambient (R-5, R-6).</summary>
    public static Colour Diffuse(Scene scene, Hit hit) =>
        // Static lambda with the scene and hit in the accumulator: no closure per shaded point.
        // .Items gives ImmutableArray's own Aggregate: no boxed ValueList or enumerator (see Intersection).
        scene.Lights.Items.Aggregate(
            (Sum: hit.Material.Colour * Ambient, Scene: scene, Hit: hit),
            static (acc, light) => (acc.Sum + Lambert(acc.Scene, acc.Hit, light), acc.Scene, acc.Hit))
        .Sum;

    private static Colour Lambert(Scene scene, Hit hit, Light light)
    {
        var origin = hit.Point + hit.Normal * Intersection.SurfaceOffset;
        var toLight = light.Position - origin;
        var distance = toLight.Length;
        var direction = toLight * (1 / distance);
        var facing = hit.Normal.Dot(direction);
        return facing <= 0 || Intersection.Blocked(scene, new Ray(origin, direction), distance)
            ? Colour.Black
            : hit.Material.Colour * light.Colour * facing;
    }

    private static Ray Reflected(Ray ray, Hit hit) =>
        new(hit.Point + hit.Normal * Intersection.SurfaceOffset, ray.Direction.Reflect(hit.Normal));
}
