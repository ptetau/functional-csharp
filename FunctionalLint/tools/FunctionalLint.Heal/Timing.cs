using System.Diagnostics;

namespace FunctionalLint.Heal;

/// <summary>
/// Where a check spends its time, written to stderr when FUNCTIONALLINT_TIMING=1. The scale test
/// (tools/scale-test.sh) uses it; it's off otherwise.
/// </summary>
public static class Timing
{
    public static bool IsOn { get; } = Environment.GetEnvironmentVariable("FUNCTIONALLINT_TIMING") == "1";


    /// <summary>Writes the time since the last mark (or the clock's start) and restarts the clock.</summary>
    public static void Mark(string step, Stopwatch clock, TimeSpan? took = null)
    {
        if (IsOn) Console.Error.WriteLine($"timing: {step}: {(took ?? clock.Elapsed).TotalSeconds:0.00}s");
        clock.Restart();
    }
}
