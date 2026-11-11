using Functional;
using RayTracer.Core;

namespace RayTracer.Tests;

/// <summary>Helpers for building scenes in tests.</summary>
internal static class Scenes
{
    public const string Sample = """
        # Output size and samples per pixel
        image 320 240 4
        camera  0 1 -5   0 1 0   60        # position, look-at point, vertical field of view in degrees
        background 0.6 0.8 1.0
        light   -5 5 -5   1 1 1             # point light: position, colour
        material red    diffuse   0.9 0.2 0.2
        material mirror reflect   0.9 0.9 0.9  0.8   # colour, reflectivity 0..1
        material floor  diffuse   0.8 0.8 0.8
        sphere  0 1 0   1       red         # centre, radius, material
        sphere  2 1 1   1       mirror
        plane   0 1 0   0       floor       # normal, distance from origin, material
        """;

    public static Scene Parse(string text) =>
        SceneParser.Parse(text).Match(
            scene => scene,
            errors => throw new InvalidOperationException(string.Join("\n", errors)));

    public static ValueList<SceneError> Errors(string text) =>
        SceneParser.Parse(text).ErrorsOrEmpty;

    public static Scene Empty(Colour background) => new(
        new ImageSpec(1, 1, 1),
        new Camera(new Vec3(0, 0, -1), Vec3.Zero, 90),
        background,
        [],
        [],
        []);

    public static Material Diffuse(double grey) => new("m", new Colour(grey, grey, grey), 0);

    public static Material Mirror(double grey, double reflectivity) => new("mirror", new Colour(grey, grey, grey), reflectivity);

    public static readonly Light WhiteLight = new(new Vec3(0, 10, 0), new Colour(1, 1, 1));
}
