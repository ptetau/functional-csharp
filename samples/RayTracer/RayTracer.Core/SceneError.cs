namespace RayTracer.Core;

/// <summary>
/// A problem in a scene file. Line is 1-based; 0 means the problem is with the file as a whole.
/// SceneParser returns Functional's Validation&lt;SceneError, Scene&gt;, which keeps every one of them (R-2).
/// </summary>
public sealed record SceneError(int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"line {Line}: {Message}" : $"scene: {Message}";
}
