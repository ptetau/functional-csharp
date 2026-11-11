namespace RayTracer.Core;

/// <summary>A point or direction in 3D space.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 Up = new(0, 1, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static Vec3 operator *(double k, Vec3 a) => a * k;

    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;

    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);

    public double Length => Math.Sqrt(Dot(this));

    public Vec3 Normalised() => this * (1.0 / Length);

    /// <summary>This direction mirrored about a surface with the given unit normal.</summary>
    public Vec3 Reflect(Vec3 normal) => this - normal * (2 * Dot(normal));
}

/// <summary>Red, green and blue, nominally 0 to 1 but unbounded while light is being added up.</summary>
public readonly record struct Colour(double R, double G, double B)
{
    public static readonly Colour Black = new(0, 0, 0);

    public static Colour operator +(Colour a, Colour b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
    public static Colour operator *(Colour a, Colour b) => new(a.R * b.R, a.G * b.G, a.B * b.B);
    public static Colour operator *(Colour a, double k) => new(a.R * k, a.G * k, a.B * k);
    public static Colour operator *(double k, Colour a) => a * k;
}

/// <summary>A half-line: everything at Origin + t × Direction for t ≥ 0. Direction is a unit vector.</summary>
public readonly record struct Ray(Vec3 Origin, Vec3 Direction)
{
    public Vec3 At(double t) => Origin + Direction * t;
}
