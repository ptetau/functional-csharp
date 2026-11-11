using System.Collections.Immutable;

namespace RayTracer.Core;

/// <summary>Output size and how many rays to fire per pixel.</summary>
public sealed record ImageSpec(int Width, int Height, int Samples);

/// <summary>Where the camera is, what it looks at and its vertical field of view in degrees.</summary>
public sealed record Camera(Vec3 Position, Vec3 LookAt, double FieldOfView);

public sealed record Light(Vec3 Position, Colour Colour);

/// <summary>
/// How a surface responds to light. Diffuse surfaces have Reflectivity 0.
/// A reflective surface mixes its diffuse shading with what it reflects (R-7).
/// </summary>
public sealed record Material(string Name, Colour Colour, double Reflectivity);

public sealed record Sphere(Vec3 Centre, double Radius, Material Material);

/// <summary>All points p with Normal · p = Distance. Normal is a unit vector.</summary>
public sealed record Plane(Vec3 Normal, double Distance, Material Material);

/// <summary>A complete, valid scene, ready to render. Build one with SceneParser.</summary>
public sealed record Scene(
    ImageSpec Image,
    Camera Camera,
    Colour Background,
    ImmutableArray<Light> Lights,
    ImmutableArray<Sphere> Spheres,
    ImmutableArray<Plane> Planes);
