using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0011_SideEffects
{
    [Fact]
    public Task Clock_identity_and_randomness_are_flagged() => Verify("""
        using System;

        public static class C
        {
            public static DateTime A() => {|FP0011:DateTime.Now|};
            public static DateTime B() => {|FP0011:DateTime.UtcNow|};
            public static DateTime D() => {|FP0011:DateTime.Today|};
            public static DateTimeOffset E() => {|FP0011:DateTimeOffset.UtcNow|};
            public static TimeProvider F() => {|FP0011:TimeProvider.System|};
            public static Guid G() => {|FP0011:Guid.NewGuid()|};
            public static int H() => {|FP0011:{|FP0011:Random.Shared|}.Next()|};
            public static Random I() => {|FP0011:new Random(1)|};
        }
        """);

    [Fact]
    public Task Io_console_environment_and_processes_are_flagged() => Verify("""
        using System;
        using System.Diagnostics;
        using System.IO;
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using static System.Console;

        public static class C
        {
            public static void A(string s) => {|FP0011:Console.WriteLine(s)|};
            public static void B(string s) => {|FP0011:WriteLine(s)|};
            public static Action<string> D() => {|FP0011:Console.WriteLine|};
            public static string E() => {|FP0011:File.ReadAllText("x")|};
            public static string F() => {|FP0011:Environment.MachineName|};
            public static HttpClient G() => {|FP0011:new HttpClient()|};
            public static Stopwatch H() => {|FP0011:Stopwatch.StartNew()|};
            public static void I() => {|FP0011:Thread.Sleep(1)|};
            public static Task J() => {|FP0011:Task.Delay(1)|};
        }
        """);

    [Fact]
    public Task Pure_members_of_the_same_types_are_allowed() => Verify("""
        using System;

        public static class C
        {
            public static DateTime Tomorrow(DateTime today) => today.AddDays(1);
            public static Guid Parse(string s) => Guid.TryParse(s, out var g) ? g : Guid.Empty;
            public static string Name() => nameof(Console);
            public static Type T() => typeof(Console);
        }
        """);

    [Fact]
    public Task Extra_types_and_members_can_be_configured() => Verify("""
        using System.IO;

        namespace Acme { public static class Mailer { public static void Send() { } } }

        public static class C
        {
            public static string Temp() => {|FP0011:Path.GetTempPath()|};
            public static string Ext(string p) => Path.GetExtension(p);
            public static void Mail() => {|FP0011:Acme.Mailer.Send()|};
        }
        """,
        "functional_lint.impure_apis = System.IO.Path.GetTempPath, Acme.Mailer");

    [Fact]
    public Task Adapters_may_use_side_effects() => Verify($$"""
        using System;

        {{AdapterAttribute}}

        [ImperativeAdapter]
        public static class Clock
        {
            public static DateTime Now() => DateTime.Now;
        }
        """);
}

public class FP0011_HiddenInputs
{
    [Fact]
    public async Task Machine_dependent_and_parallel_apis_say_why()
    {
        var findings = await Diagnose("""
            using System.Linq;
            static class C
            {
                static string A() => System.Environment.NewLine;
                static System.Globalization.CultureInfo B() => System.Globalization.CultureInfo.CurrentCulture;
                static int D(int[] xs) => xs.AsParallel().Sum();
            }
            """);

        Assert.Equal(new[] {
            "'Environment.NewLine' depends on the machine it runs on. Call it from an [ImperativeAdapter], or pass the value in.",
            "'CultureInfo.CurrentCulture' depends on the machine it runs on. Call it from an [ImperativeAdapter], or pass the value in.",
            "'ParallelEnumerable.AsParallel' runs work in an unpredictable order. Call it from an [ImperativeAdapter], or pass the value in.",
            "'ParallelEnumerable.Sum' runs work in an unpredictable order. Call it from an [ImperativeAdapter], or pass the value in.",
        }.Order(), findings.Where(f => f.Id == "FP0011").Select(f => f.Message).Order());
    }
}

public class FP0011_MoreApis
{
    [Fact]
    public Task Time_based_guids_and_temp_files_are_side_effects() => Verify("""
        static class C
        {
            static System.Guid A() => {|FP0011:System.Guid.CreateVersion7()|};
            static string B() => {|FP0011:System.IO.Path.GetTempFileName()|};
            static string D() => {|FP0011:System.IO.Path.GetRandomFileName()|};
        }
        """);
}

public class FP0011_IndentedJson
{
    [Fact]
    public Task Indented_json_without_a_newline_is_flagged() => Verify("""
        using System.Text.Json;

        public static class C
        {
            public static readonly JsonSerializerOptions A = new() { {|FP0011:WriteIndented = true|} };
            public static readonly JsonSerializerOptions B = new(JsonSerializerDefaults.Web) { {|FP0011:WriteIndented = true|} };
            public static readonly JsonWriterOptions D = new() { {|FP0011:Indented = true|} };
        }
        """);

    [Fact]
    public Task Options_that_set_a_newline_or_dont_indent_are_fine() => Verify("""
        using System.Text.Json;

        public static class C
        {
            public static readonly JsonSerializerOptions A = new() { WriteIndented = true, NewLine = "\n" };
            public static readonly JsonSerializerOptions B = new() { NewLine = "\n", WriteIndented = true };
            public static readonly JsonSerializerOptions D = new() { WriteIndented = false };
            public static readonly JsonSerializerOptions E = new() { PropertyNameCaseInsensitive = true };
            public static readonly JsonWriterOptions F = new() { Indented = true, NewLine = "\n" };
        }
        """);
}
