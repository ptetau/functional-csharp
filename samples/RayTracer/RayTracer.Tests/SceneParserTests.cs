using RayTracer.Core;

namespace RayTracer.Tests;

public class SceneParserTests
{
    [Fact]
    public void R1_parses_the_sample_scene()
    {
        var scene = Scenes.Parse(Scenes.Sample);

        Assert.Equal(new ImageSpec(320, 240, 4), scene.Image);
        Assert.Equal(new Camera(new Vec3(0, 1, -5), new Vec3(0, 1, 0), 60), scene.Camera);
        Assert.Equal(new Colour(0.6, 0.8, 1.0), scene.Background);
        Assert.Equal([new Light(new Vec3(-5, 5, -5), new Colour(1, 1, 1))], scene.Lights.ToArray());
        Assert.Equal(2, scene.Spheres.Length);
        Assert.Equal(new Material("mirror", new Colour(0.9, 0.9, 0.9), 0.8), scene.Spheres[1].Material);
        Assert.Equal(new Plane(new Vec3(0, 1, 0), 0, new Material("floor", new Colour(0.8, 0.8, 0.8), 0)), scene.Planes.Single());
    }

    [Fact]
    public void R1_items_may_appear_in_any_order()
    {
        var reversed = string.Join("\n", Scenes.Sample.Split('\n').Reverse());

        Assert.Equal(Renderer.Render(Scenes.Parse(Scenes.Sample)).AsSpan().ToArray(),
                     Renderer.Render(Scenes.Parse(reversed)).AsSpan().ToArray());
    }

    [Fact]
    public void R1_image_and_camera_are_required()
    {
        var errors = Scenes.Errors("background 0 0 0");

        Assert.Equal(2, errors.Length);
        Assert.All(errors, e => Assert.Equal(0, e.Line));
        Assert.Contains(errors, e => e.Message.StartsWith("'image' is required"));
        Assert.Contains(errors, e => e.Message.StartsWith("'camera' is required"));
    }

    [Fact]
    public void R1_every_material_named_by_a_shape_must_be_defined()
    {
        var errors = Scenes.Errors("""
            image 2 2 1
            camera 0 0 -1 0 0 0 90
            sphere 0 0 0 1 gold
            plane 0 1 0 0 grass
            """);

        Assert.Equal(
            ["line 3: material 'gold' is not defined", "line 4: material 'grass' is not defined"],
            errors.Select(e => e.ToString()));
    }

    [Fact]
    public void R1_background_is_optional_and_defaults_to_black()
    {
        var scene = Scenes.Parse("image 1 1 1\ncamera 0 0 -1 0 0 0 90");

        Assert.Equal(Colour.Black, scene.Background);
    }

    [Fact]
    public void R2_reports_every_error_with_its_line_number()
    {
        var errors = Scenes.Errors("""
            image 320 abc 4
            # a comment

            camera 0 0 -1 0 0 0 200
            light 1 2 3 1 1
            material m shiny 1 1 1
            sphere 0 0 0 -1 m
            cube 1 2 3
            material m diffuse 1 1 1
            material m diffuse 1 1 1
            background 2 0 0
            """);

        Assert.Equal(
            [
                "line 1: height must be a whole number greater than 0, not 'abc'",
                "line 4: field of view must be between 0 and 180 degrees",
                "line 5: expected light <x y z> <r g b>",
                "line 6: expected material <name> diffuse <r g b> or material <name> reflect <r g b> <reflectivity>",
                "line 7: radius must be greater than 0",
                "line 8: unknown item 'cube'. Expected image, camera, background, light, material, sphere or plane",
                "line 10: material 'm' is already defined on line 9",
                "line 11: background colour values must be between 0 and 1",
            ],
            errors.Select(e => e.ToString()));
    }

    [Fact]
    public void R2_reports_duplicate_image_and_camera_lines()
    {
        var errors = Scenes.Errors("image 1 1 1\ncamera 0 0 -1 0 0 0 90\nimage 2 2 1\ncamera 0 0 -2 0 0 0 90");

        Assert.Equal(
            ["line 3: image is already defined on line 1", "line 4: camera is already defined on line 2"],
            errors.Select(e => e.ToString()));
    }

    [Fact]
    public void R2_rejects_reflectivity_outside_0_to_1_and_a_camera_looking_at_itself()
    {
        var errors = Scenes.Errors("""
            image 1 1 1
            camera 1 1 1 1 1 1 90
            material m reflect 1 1 1 1.5
            """);

        Assert.Equal(
            ["line 2: camera position and look-at point must differ", "line 3: reflectivity must be between 0 and 1"],
            errors.Select(e => e.ToString()));
    }

    [Fact]
    public void Accepts_windows_line_endings_and_trailing_comments()
    {
        var scene = Scenes.Parse("image 4 3 1   # size\r\ncamera 0 0 -1 0 0 0 90\r\nbackground 0 0.5 1#blue\r\n");

        Assert.Equal(new ImageSpec(4, 3, 1), scene.Image);
        Assert.Equal(new Colour(0, 0.5, 1), scene.Background);
    }
}
