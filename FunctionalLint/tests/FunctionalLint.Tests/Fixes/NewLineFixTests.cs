using System.Threading.Tasks;
using FunctionalLint.CodeFixes;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Fixes;

public class NewLineFixTests
{
    [Fact]
    public Task The_newline_goes_after_WriteIndented_on_its_own_line() => VerifyFix<NewLineFix>("""
        using System.Text.Json;

        public static class C
        {
            public static readonly JsonSerializerOptions A = new()
            {
                {|FP0011:WriteIndented = true|},
                PropertyNameCaseInsensitive = true,
            };

            public static readonly JsonSerializerOptions B = new()
            {
                PropertyNameCaseInsensitive = true,
                {|FP0011:WriteIndented = true|}
            };

            public static readonly JsonSerializerOptions D = new()
            {
                {|FP0011:WriteIndented = true|}
            };
        }
        """, """
        using System.Text.Json;

        public static class C
        {
            public static readonly JsonSerializerOptions A = new()
            {
                WriteIndented = true,
                NewLine = "\n",
                PropertyNameCaseInsensitive = true,
            };

            public static readonly JsonSerializerOptions B = new()
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                NewLine = "\n"
            };

            public static readonly JsonSerializerOptions D = new()
            {
                WriteIndented = true,
                NewLine = "\n"
            };
        }
        """);

    [Fact]
    public Task One_line_initializers_stay_on_one_line() => VerifyFix<NewLineFix>("""
        using System.Text.Json;

        public static class C
        {
            public static readonly JsonSerializerOptions A = new() { {|FP0011:WriteIndented = true|} };
            public static readonly JsonSerializerOptions B = new() { PropertyNameCaseInsensitive = true, {|FP0011:WriteIndented = true|} };
            public static readonly JsonWriterOptions D = new() { {|FP0011:Indented = true|}, MaxDepth = 8 };
        }
        """, """
        using System.Text.Json;

        public static class C
        {
            public static readonly JsonSerializerOptions A = new() { WriteIndented = true, NewLine = "\n" };
            public static readonly JsonSerializerOptions B = new() { PropertyNameCaseInsensitive = true, WriteIndented = true, NewLine = "\n" };
            public static readonly JsonWriterOptions D = new() { Indented = true, NewLine = "\n", MaxDepth = 8 };
        }
        """);

    [Fact]
    public Task Xml_settings_get_NewLineChars_and_ReplaceLineEndings_gets_an_argument() => VerifyFix<NewLineFix>("""
        using System.Xml;

        public static class C
        {
            public static readonly XmlWriterSettings Settings = new() { {|FP0011:Indent = true|} };

            public static string Normalise(string text) => {|FP0011:text.ReplaceLineEndings()|};
        }
        """, """
        using System.Xml;

        public static class C
        {
            public static readonly XmlWriterSettings Settings = new() { Indent = true, NewLineChars = "\n" };

            public static string Normalise(string text) => text.ReplaceLineEndings("\n");
        }
        """);

    [Fact]
    public Task Other_side_effects_get_no_fix() => VerifyFix<NewLineFix>("""
        public static class C
        {
            public static System.DateTime Now() => {|FP0011:System.DateTime.Now|};
        }
        """, """
        public static class C
        {
            public static System.DateTime Now() => {|FP0011:System.DateTime.Now|};
        }
        """);
}
