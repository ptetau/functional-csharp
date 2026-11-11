// The imperative shell: read the scene file, hand it to the core, write the bytes, report the time.
// Splitting rows across threads is wiring; the core decides the bands and renders each one.
using System.Diagnostics;
using RayTracer.Core;

var positional = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || args[i - 1] != "--threads")).ToArray();
var threadsAt = Array.IndexOf(args, "--threads");
var threads = threadsAt >= 0 && threadsAt + 1 < args.Length && int.TryParse(args[threadsAt + 1], out var n) && n > 0
    ? n
    : threadsAt >= 0 ? -1 : 1;

if (positional.Length != 2 || threads < 1)
{
    Console.Error.WriteLine("usage: raytrace <scene-file> <output.ppm> [--threads <n>]");
    return 2;
}

string text;
try
{
    text = File.ReadAllText(positional[0]);
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"can't read {positional[0]}: {e.Message}");
    return 1;
}

var clock = Stopwatch.StartNew();
var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

return SceneParser.Parse(text).Match(
    scene =>
    {
        var bands = Renderer.Bands(scene.Image.Height, threads);
        var rendered = new System.Collections.Immutable.ImmutableArray<byte>[bands.Length];
        Parallel.For(0, bands.Length, new ParallelOptions { MaxDegreeOfParallelism = threads },
            i => rendered[i] = Renderer.RenderRows(scene, bands[i]));
        var ppm = Ppm.Encode(scene.Image, rendered);
        var elapsed = clock.Elapsed;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        File.WriteAllBytes(positional[1], ppm.AsSpan());
        Console.WriteLine(
            $"Rendered {scene.Image.Width}x{scene.Image.Height} at {scene.Image.Samples} samples per pixel " +
            $"on {threads} thread(s) in {elapsed.TotalSeconds:0.000} s ({allocated / 1048576.0:0} MB allocated).");
        return 0;
    },
    errors =>
    {
        foreach (var error in errors)
            Console.Error.WriteLine(error);
        return 1;
    });
