using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace FunctionalLint.Tests.Guidance;

/// <summary>
/// Checks the rules are well formed, and that the code, README, rule docs and tests agree with each other.
/// These stop the catalogue drifting as rules are added or changed.
/// </summary>
public class RuleCatalogTests
{
    private static readonly IReadOnlyList<DiagnosticDescriptor> Rules = new FunctionalAnalyzer().SupportedDiagnostics;

    public static TheoryData<string> RuleIds => new(Rules.Select(r => r.Id));

    private static DiagnosticDescriptor Rule(string id) => Rules.Single(r => r.Id == id);

    [Fact]
    public void Ids_are_unique_and_numbered_in_order_from_FP0001()
    {
        var expected = Enumerable.Range(1, Rules.Count).Select(n => $"FP{n:0000}");
        Assert.Equal(expected, Rules.Select(r => r.Id));
    }

    [Fact]
    public void Every_public_descriptor_is_registered()
    {
        var declared = typeof(FunctionalAnalyzer).GetFields()
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => ((DiagnosticDescriptor)f.GetValue(null)!).Id);
        Assert.Equal(declared.OrderBy(x => x), Rules.Select(r => r.Id).OrderBy(x => x));
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void Is_an_error_in_the_Functional_category_and_on_by_default(string id)
    {
        var rule = Rule(id);
        Assert.Equal("Functional", rule.Category);
        Assert.Equal(DiagnosticSeverity.Error, rule.DefaultSeverity);
        Assert.True(rule.IsEnabledByDefault);
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void Has_a_title_message_and_description(string id)
    {
        var rule = Rule(id);
        var title = rule.Title.ToString();
        var description = rule.Description.ToString();

        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.False(title.EndsWith('.'), "Titles are labels, not sentences.");
        // Rendered messages are checked in MessageTests; here just make sure the format can't end mid-sentence.
        Assert.True(rule.MessageFormat.ToString() is var format && (format.EndsWith('.') || format.EndsWith('}')));
        Assert.True(description.Length >= 60, $"{id}: the description should explain why the rule exists and what to do.");
        Assert.EndsWith(".", description);
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void Help_link_points_at_a_rule_doc_that_exists(string id)
    {
        var rule = Rule(id);
        Assert.Equal(FunctionalAnalyzer.HelpLinkBase + id + ".md", rule.HelpLinkUri);
        Assert.True(Repo.Exists("docs", "rules", id + ".md"), $"Write docs/rules/{id}.md");
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void Rule_doc_has_the_standard_sections(string id)
    {
        var doc = Repo.Read("docs", "rules", id + ".md");
        Assert.StartsWith($"# {id}: {Rule(id).Title}", doc);
        foreach (var section in new[] { "## Why", "## Instead", "## Auto-fix" })
            Assert.Contains(section, doc);

        // Before/after examples, in C#.
        Assert.True(Regex.Matches(doc, "```csharp").Count >= 2, $"{id}.md needs a 'before' and an 'after' example.");
    }

    [Fact]
    public void Readme_rule_table_lists_exactly_the_registered_rules()
    {
        var readme = Repo.Read("README.md");
        var inTable = Regex.Matches(readme, @"^\| \[?(FP\d{4})", RegexOptions.Multiline).Select(m => m.Groups[1].Value);
        Assert.Equal(Rules.Select(r => r.Id), inTable);
    }

    [Fact]
    public void Every_config_key_in_the_globalconfig_is_documented_in_the_readme()
    {
        var keys = Regex.Matches(Repo.Read("build", "functional.globalconfig"), @"^(functional_lint\.\w+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(keys);
        var readme = Repo.Read("README.md");
        Assert.All(keys, key => Assert.Contains(key, readme));
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void Every_rule_has_a_test_that_expects_it(string id)
    {
        // Markup for rules reported in source; `Id == "FP00xx"` for ones about the whole project (no location).
        var marker = "{|" + id + ":";
        var byId = $"Id == \"{id}\"";
        Assert.True(Repo.TestSources().Any(f => File.ReadAllText(f) is var text && (text.Contains(marker) || text.Contains(byId))),
            $"No test expects {id}. Add one under tests/FunctionalLint.Tests/Rules.");
    }
}
