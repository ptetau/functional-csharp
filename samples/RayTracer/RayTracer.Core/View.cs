using System.Collections.Immutable;

namespace RayTracer.Core;

/// <summary>A sample position inside a pixel: (0, 0) is its top-left corner, (1, 1) its bottom-right.</summary>
public readonly record struct Offset(double X, double Y);

/// <summary>The camera set up for one image size: turns pixel positions into rays.</summary>
public readonly record struct View(Vec3 Origin, Vec3 Forward, Vec3 Right, Vec3 Up, double HalfWidth, double HalfHeight, int Width, int Height)
{
    /// <summary>
    /// The world's up is +Y. Right is up × forward, so with the camera looking along +Z, +X is to the right.
    /// If the camera looks straight up or down, +Z stands in for up.
    /// </summary>
    public static View Of(Camera camera, ImageSpec image)
    {
        var forward = (camera.LookAt - camera.Position).Normalised();
        var worldUp = Math.Abs(forward.Dot(Vec3.Up)) > 1 - 1e-9 ? new Vec3(0, 0, 1) : Vec3.Up;
        var right = worldUp.Cross(forward).Normalised();
        var up = forward.Cross(right);
        var halfHeight = Math.Tan(camera.FieldOfView * Math.PI / 360);
        return new View(camera.Position, forward, right, up, halfHeight * image.Width / image.Height, halfHeight, image.Width, image.Height);
    }

    /// <summary>The ray through a point in the image, measured in pixels from its top-left corner.</summary>
    public Ray Through(double x, double y)
    {
        var across = (2 * x / Width - 1) * HalfWidth;
        var down = (1 - 2 * y / Height) * HalfHeight;
        return new Ray(Origin, (Forward + Right * across + Up * down).Normalised());
    }
}

public static class Sampling
{
    /// <summary>
    /// Where to fire rays inside each pixel (R-3): a k × k grid of cell centres when samples = k²,
    /// otherwise evenly spaced along the top-left to bottom-right diagonal.
    /// </summary>
    public static ImmutableArray<Offset> Offsets(int samples) =>
        (int)Math.Round(Math.Sqrt(samples)) is var side && side > 0 && side * side == samples
            ? Enumerable.Range(0, samples)
                .Select(i => new Offset((i % side + 0.5) / side, (i / side + 0.5) / side))
                .ToImmutableArray()
            : Enumerable.Range(0, samples)
                .Select(i => new Offset((i + 0.5) / samples, (i + 0.5) / samples))
                .ToImmutableArray();
}
