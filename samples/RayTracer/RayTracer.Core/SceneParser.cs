using System.Collections.Immutable;
using System.Globalization;
using Functional;

namespace RayTracer.Core;

/// <summary>Turns scene file text into a Scene, or every problem in it with its line number (R-1, R-2).</summary>
public static class SceneParser
{
    // One parsed line. Each kind is collected with OfType, so no switch over the cases is needed.
    private closed record Item(int Line);
    private sealed record Blank(int Line) : Item(Line);
    private sealed record Bad(int Line, string Keyword, string Message) : Item(Line);
    private sealed record ImageItem(int Line, ImageSpec Image) : Item(Line);
    private sealed record CameraItem(int Line, Camera Camera) : Item(Line);
    private sealed record BackgroundItem(int Line, Colour Colour) : Item(Line);
    private sealed record LightItem(int Line, Light Light) : Item(Line);
    private sealed record MaterialItem(int Line, Material Material) : Item(Line);
    private sealed record SphereItem(int Line, Vec3 Centre, double Radius, string Material) : Item(Line);
    private sealed record PlaneItem(int Line, Vec3 Normal, double Distance, string Material) : Item(Line);

    public static Validation<SceneError, Scene> Parse(string text) =>
        Build(text.Split('\n').Select((line, index) => ParseLine(index + 1, line)).ToImmutableArray());

    // Lines ------------------------------------------------------------------

    private static Item ParseLine(int line, string raw) =>
        WithoutComment(raw).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is [var keyword, .. var rest]
            ? ParseItem(line, keyword, rest).Match(item => item, error => new Bad(line, keyword, error.Message))
            : new Blank(line);

    private static string WithoutComment(string raw) =>
        raw.IndexOf('#') is var hash and >= 0 ? raw[..hash] : raw;

    // Each item's shape is a list pattern, so a token is only read once the line is known to have it.
    private static Result<Item> ParseItem(int line, string keyword, string[] a) => (keyword, a) switch
    {
        ("image", [var w, var h, var s]) =>
            from width in Whole(w, "width")
            from height in Whole(h, "height")
            from samples in Whole(s, "samples")
            select (Item)new ImageItem(line, new ImageSpec(width, height, samples)),
        ("image", _) => Usage("image <width> <height> <samples>"),
        ("camera", [var x, var y, var z, var lx, var ly, var lz, var f]) =>
            from position in Vector(x, y, z, "camera position")
            from lookAt in Vector(lx, ly, lz, "camera look-at point")
            from fov in Number(f, "field of view")
            from __ in Check(fov is > 0 and < 180, "field of view must be between 0 and 180 degrees")
            from ___ in Check(position != lookAt, "camera position and look-at point must differ")
            select (Item)new CameraItem(line, new Camera(position, lookAt, fov)),
        ("camera", _) => Usage("camera <x y z> <look-at x y z> <field of view>"),
        ("background", [var r, var g, var b]) =>
            from colour in ColourOf(r, g, b, "background colour")
            select (Item)new BackgroundItem(line, colour),
        ("background", _) => Usage("background <r g b>"),
        ("light", [var x, var y, var z, var r, var g, var b]) =>
            from position in Vector(x, y, z, "light position")
            from colour in ColourOf(r, g, b, "light colour")
            select (Item)new LightItem(line, new Light(position, colour)),
        ("light", _) => Usage("light <x y z> <r g b>"),
        ("material", [var name, "diffuse", var r, var g, var b]) =>
            from colour in ColourOf(r, g, b, "material colour")
            select (Item)new MaterialItem(line, new Material(name, colour, 0)),
        ("material", [_, "diffuse", ..]) => Usage("material <name> diffuse <r g b>"),
        ("material", [var name, "reflect", var r, var g, var b, var k]) =>
            from colour in ColourOf(r, g, b, "material colour")
            from reflectivity in Number(k, "reflectivity")
            from __ in Check(reflectivity is >= 0 and <= 1, "reflectivity must be between 0 and 1")
            select (Item)new MaterialItem(line, new Material(name, colour, reflectivity)),
        ("material", [_, "reflect", ..]) => Usage("material <name> reflect <r g b> <reflectivity>"),
        ("material", _) => new Error("material", "expected material <name> diffuse <r g b> or material <name> reflect <r g b> <reflectivity>"),
        ("sphere", [var x, var y, var z, var r, var material]) =>
            from centre in Vector(x, y, z, "sphere centre")
            from radius in Number(r, "radius")
            from __ in Check(radius > 0, "radius must be greater than 0")
            select (Item)new SphereItem(line, centre, radius, material),
        ("sphere", _) => Usage("sphere <x y z> <radius> <material>"),
        ("plane", [var x, var y, var z, var d, var material]) =>
            from normal in Vector(x, y, z, "plane normal")
            from __ in Check(normal.Length > 0, "plane normal must not be zero")
            from distance in Number(d, "distance")
            select (Item)new PlaneItem(line, normal.Normalised(), distance, material),
        ("plane", _) => Usage("plane <normal x y z> <distance> <material>"),
        _ => new Error("keyword", $"unknown item '{keyword}'. Expected image, camera, background, light, material, sphere or plane"),
    };

    // Tokens -----------------------------------------------------------------

    private static Result<Item> Usage(string usage) => new Error("arity", $"expected {usage}");

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

    private static Result<Vec3> Vector(string xs, string ys, string zs, string what) =>
        from x in Number(xs, what)
        from y in Number(ys, what)
        from z in Number(zs, what)
        select new Vec3(x, y, z);

    private static Result<Colour> ColourOf(string r, string g, string b, string what) =>
        from v in Vector(r, g, b, what)
        from _ in Check(new[] { v.X, v.Y, v.Z }.All(c => c is >= 0 and <= 1), $"{what} values must be between 0 and 1")
        select new Colour(v.X, v.Y, v.Z);

    // The whole file ---------------------------------------------------------

    private static Validation<SceneError, Scene> Build(ImmutableArray<Item> items)
    {
        var images = items.OfType<ImageItem>().ToImmutableArray();
        var cameras = items.OfType<CameraItem>().ToImmutableArray();
        var backgrounds = items.OfType<BackgroundItem>().ToImmutableArray();
        var materials = items.OfType<MaterialItem>().ToImmutableArray();

        // A lookup never throws on a duplicate name or a missing one; the first definition wins and the rest are reported.
        var materialsByName = materials.ToLookup(m => m.Material.Name, m => m.Material);

        var problems = items.OfType<Bad>().Select(b => new SceneError(b.Line, b.Message))
            .Concat(Required(items, images, "image", "image <width> <height> <samples>"))
            .Concat(Required(items, cameras, "camera", "camera <x y z> <look-at x y z> <field of view>"))
            .Concat(Duplicates(images, _ => "image"))
            .Concat(Duplicates(cameras, _ => "camera"))
            .Concat(Duplicates(backgrounds, _ => "background"))
            .Concat(Duplicates(materials, m => $"material '{m.Material.Name}'"))
            .ToValueList();

        var header = problems.IsEmpty && images is [var image, ..] && cameras is [var camera, ..]
            ? Validation.Valid<SceneError, (ImageSpec Image, Camera Camera)>((image.Image, camera.Camera))
            : new Validation<SceneError, (ImageSpec Image, Camera Camera)>.Invalid(problems);
        var spheres = items.OfType<SphereItem>().Traverse(s =>
            Resolve(materialsByName, s.Line, s.Material).Map(m => new Sphere(s.Centre, s.Radius, m)));
        var planes = items.OfType<PlaneItem>().Traverse(p =>
            Resolve(materialsByName, p.Line, p.Material).Map(m => new Plane(p.Normal, p.Distance, m)));

        return Validation.Combine(header, spheres, planes, (h, s, p) => new Scene(
                h.Image,
                h.Camera,
                backgrounds is [var background, ..] ? background.Colour : Colour.Black,
                items.OfType<LightItem>().Select(l => l.Light).ToValueList(),
                s,
                p))
            .Match<Validation<SceneError, Scene>>(
                scene => scene,
                errors => new Validation<SceneError, Scene>.Invalid(errors.OrderBy(e => e.Line).ToValueList()));
    }

    // A line that tried to give the item but had an error has already been reported.
    private static IEnumerable<SceneError> Required<T>(ImmutableArray<Item> items, ImmutableArray<T> found, string name, string usage) =>
        found.IsEmpty && !items.OfType<Bad>().Any(b => b.Keyword == name)
            ? [new SceneError(0, $"'{name}' is required: {usage}")]
            : [];

    private static IEnumerable<SceneError> Duplicates<T>(ImmutableArray<T> found, Func<T, string> key) where T : Item =>
        found
            .GroupBy(key)
            .SelectMany(g => g.ToImmutableArray() is [var first, .. var rest]
                ? rest.Select(dup => new SceneError(dup.Line, $"{g.Key} is already defined on line {first.Line}"))
                : []);

    private static Validation<SceneError, Material> Resolve(ILookup<string, Material> materials, int line, string name) =>
        materials[name].FirstOrDefault() is { } material
            ? material
            : Validation.Invalid<SceneError, Material>(Undefined(line, name));

    private static SceneError Undefined(int line, string material) =>
        new(line, $"material '{material}' is not defined");
}
