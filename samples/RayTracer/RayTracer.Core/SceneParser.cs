using System.Collections.Immutable;
using System.Globalization;
using Functional;

namespace RayTracer.Core;

/// <summary>Turns scene file text into a Scene, or every problem in it with its line number (R-1, R-2).</summary>
public static class SceneParser
{
    // One parsed line. Each kind is collected with OfType, so no switch over the cases is needed.
    private abstract record Item(int Line);
    private sealed record Blank(int Line) : Item(Line);
    private sealed record Bad(int Line, string Keyword, string Message) : Item(Line);
    private sealed record ImageItem(int Line, ImageSpec Image) : Item(Line);
    private sealed record CameraItem(int Line, Camera Camera) : Item(Line);
    private sealed record BackgroundItem(int Line, Colour Colour) : Item(Line);
    private sealed record LightItem(int Line, Light Light) : Item(Line);
    private sealed record MaterialItem(int Line, Material Material) : Item(Line);
    private sealed record SphereItem(int Line, Vec3 Centre, double Radius, string Material) : Item(Line);
    private sealed record PlaneItem(int Line, Vec3 Normal, double Distance, string Material) : Item(Line);

    public static Validation<Scene> Parse(string text) =>
        Build(text.Split('\n').Select((line, index) => ParseLine(index + 1, line)).ToImmutableArray());

    // Lines ------------------------------------------------------------------

    private static Item ParseLine(int line, string raw)
    {
        var tokens = WithoutComment(raw).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0
            ? new Blank(line)
            : ParseItem(line, tokens[0], tokens[1..]).Match(item => item, error => new Bad(line, tokens[0], error.Message));
    }

    private static string WithoutComment(string raw) =>
        raw.IndexOf('#') is var hash and >= 0 ? raw[..hash] : raw;

    private static Result<Item> ParseItem(int line, string keyword, string[] a) => keyword switch
    {
        "image" =>
            from _ in Arity(a, 3, "image <width> <height> <samples>")
            from width in Whole(a[0], "width")
            from height in Whole(a[1], "height")
            from samples in Whole(a[2], "samples")
            select (Item)new ImageItem(line, new ImageSpec(width, height, samples)),
        "camera" =>
            from _ in Arity(a, 7, "camera <x y z> <look-at x y z> <field of view>")
            from position in Vector(a, 0, "camera position")
            from lookAt in Vector(a, 3, "camera look-at point")
            from fov in Number(a[6], "field of view")
            from __ in Check(fov is > 0 and < 180, "field of view must be between 0 and 180 degrees")
            from ___ in Check(position != lookAt, "camera position and look-at point must differ")
            select (Item)new CameraItem(line, new Camera(position, lookAt, fov)),
        "background" =>
            from _ in Arity(a, 3, "background <r g b>")
            from colour in ColourAt(a, 0, "background colour")
            select (Item)new BackgroundItem(line, colour),
        "light" =>
            from _ in Arity(a, 6, "light <x y z> <r g b>")
            from position in Vector(a, 0, "light position")
            from colour in ColourAt(a, 3, "light colour")
            select (Item)new LightItem(line, new Light(position, colour)),
        "material" => ParseMaterial(line, a),
        "sphere" =>
            from _ in Arity(a, 5, "sphere <x y z> <radius> <material>")
            from centre in Vector(a, 0, "sphere centre")
            from radius in Number(a[3], "radius")
            from __ in Check(radius > 0, "radius must be greater than 0")
            select (Item)new SphereItem(line, centre, radius, a[4]),
        "plane" =>
            from _ in Arity(a, 5, "plane <normal x y z> <distance> <material>")
            from normal in Vector(a, 0, "plane normal")
            from __ in Check(normal.Length > 0, "plane normal must not be zero")
            from distance in Number(a[3], "distance")
            select (Item)new PlaneItem(line, normal.Normalised(), distance, a[4]),
        _ => new Error("keyword", $"unknown item '{keyword}'. Expected image, camera, background, light, material, sphere or plane"),
    };

    private static Result<Item> ParseMaterial(int line, string[] a) =>
        a.Length >= 2 && a[1] == "diffuse"
            ? from _ in Arity(a, 5, "material <name> diffuse <r g b>")
              from colour in ColourAt(a, 2, "material colour")
              select (Item)new MaterialItem(line, new Material(a[0], colour, 0))
        : a.Length >= 2 && a[1] == "reflect"
            ? from _ in Arity(a, 6, "material <name> reflect <r g b> <reflectivity>")
              from colour in ColourAt(a, 2, "material colour")
              from reflectivity in Number(a[5], "reflectivity")
              from __ in Check(reflectivity is >= 0 and <= 1, "reflectivity must be between 0 and 1")
              select (Item)new MaterialItem(line, new Material(a[0], colour, reflectivity))
            : new Error("material", "expected material <name> diffuse <r g b> or material <name> reflect <r g b> <reflectivity>");

    // Tokens -----------------------------------------------------------------

    private static Result<bool> Arity(string[] a, int count, string usage) =>
        a.Length == count ? true : new Error("arity", $"expected {usage}");

    private static Result<bool> Check(bool holds, string message) =>
        holds ? true : new Error("invalid", message);

    private static Result<double> Number(string token, string what) =>
        double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : new Error("number", $"{what} must be a number, not '{token}'");

    private static Result<int> Whole(string token, string what) =>
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : new Error("whole", $"{what} must be a whole number greater than 0, not '{token}'");

    private static Result<Vec3> Vector(string[] a, int at, string what) =>
        from x in Number(a[at], what)
        from y in Number(a[at + 1], what)
        from z in Number(a[at + 2], what)
        select new Vec3(x, y, z);

    private static Result<Colour> ColourAt(string[] a, int at, string what) =>
        from v in Vector(a, at, what)
        from _ in Check(new[] { v.X, v.Y, v.Z }.All(c => c is >= 0 and <= 1), $"{what} values must be between 0 and 1")
        select new Colour(v.X, v.Y, v.Z);

    // The whole file ---------------------------------------------------------

    private static Validation<Scene> Build(ImmutableArray<Item> items)
    {
        var images = items.OfType<ImageItem>().ToImmutableArray();
        var cameras = items.OfType<CameraItem>().ToImmutableArray();
        var backgrounds = items.OfType<BackgroundItem>().ToImmutableArray();
        var materials = items.OfType<MaterialItem>().ToImmutableArray();
        var spheres = items.OfType<SphereItem>().ToImmutableArray();
        var planes = items.OfType<PlaneItem>().ToImmutableArray();

        // ToImmutableDictionary throws on a duplicate key, so keep the first of each name and report the rest.
        var materialsByName = materials
            .GroupBy(m => m.Material.Name)
            .ToImmutableDictionary(g => g.Key, g => g.First().Material);

        var errors = items.OfType<Bad>().Select(b => new SceneError(b.Line, b.Message))
            .Concat(Required(items, images, "image", "image <width> <height> <samples>"))
            .Concat(Required(items, cameras, "camera", "camera <x y z> <look-at x y z> <field of view>"))
            .Concat(Duplicates(images, _ => "image"))
            .Concat(Duplicates(cameras, _ => "camera"))
            .Concat(Duplicates(backgrounds, _ => "background"))
            .Concat(Duplicates(materials, m => $"material '{m.Material.Name}'"))
            .Concat(spheres.Where(s => !materialsByName.ContainsKey(s.Material)).Select(s => Undefined(s.Line, s.Material)))
            .Concat(planes.Where(p => !materialsByName.ContainsKey(p.Material)).Select(p => Undefined(p.Line, p.Material)))
            .OrderBy(e => e.Line)
            .ToImmutableArray();

        return errors.IsEmpty && images.FirstOrDefault() is { } image && cameras.FirstOrDefault() is { } camera
            ? new Validation<Scene>.Valid(new Scene(
                image.Image,
                camera.Camera,
                backgrounds.FirstOrDefault() is { } background ? background.Colour : Colour.Black,
                items.OfType<LightItem>().Select(l => l.Light).ToImmutableArray(),
                spheres.Select(s => new Sphere(s.Centre, s.Radius, materialsByName[s.Material])).ToImmutableArray(),
                planes.Select(p => new Plane(p.Normal, p.Distance, materialsByName[p.Material])).ToImmutableArray()))
            : new Validation<Scene>.Invalid(errors);
    }

    // A line that tried to give the item but had an error has already been reported.
    private static IEnumerable<SceneError> Required<T>(ImmutableArray<Item> items, ImmutableArray<T> found, string name, string usage) =>
        found.IsEmpty && !items.OfType<Bad>().Any(b => b.Keyword == name)
            ? [new SceneError(0, $"'{name}' is required: {usage}")]
            : [];

    private static IEnumerable<SceneError> Duplicates<T>(ImmutableArray<T> found, Func<T, string> key) where T : Item =>
        found
            .GroupBy(key)
            .SelectMany(g => g.Skip(1).Select(dup =>
                new SceneError(dup.Line, $"{g.Key} is already defined on line {g.First().Line}")));

    private static SceneError Undefined(int line, string material) =>
        new(line, $"material '{material}' is not defined");
}
