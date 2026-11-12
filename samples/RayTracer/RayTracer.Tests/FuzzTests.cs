using RayTracer.Core;
using Samples.TestSupport;
using Xunit;

namespace RayTracer.Tests;

/// <summary>Any scene file gives a scene or a list of errors. Nothing throws.</summary>
public class FuzzTests
{
    [Fact]
    public void The_scene_parser_handles_mangled_scenes() =>
        Fuzz.NeverThrows(Fuzz.Around(Scenes.Sample, 3000, seed: 6), text => SceneParser.Parse(text));
}
