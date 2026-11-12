using Samples.TestSupport;
using Xunit;
using static Ledger.Tests.Harness;

namespace Ledger.Tests;

/// <summary>The core turns any journal or command line into an outcome. It never throws (FP0015 covers the common
/// throwing calls; these find the rest).</summary>
public class FuzzTests
{
    [Fact]
    public void Every_command_handles_a_mangled_journal() =>
        Fuzz.NeverThrows(Fuzz.Around(TypoJournal, 1500, seed: 1), journal =>
        {
            Run(journal, "balance");
            Run(journal, "register");
            Run(journal, "check");
            Run(journal, "add", "2026-02-02", "Shop", "Expenses:Groceries", "45.10", "Assets:Bank:Everyday");
        });

    [Fact]
    public void Add_handles_mangled_arguments() =>
        Fuzz.NeverThrows(Fuzz.Around("2026-02-02|Split|Expenses:Groceries|10|Assets:Bank:Everyday|-10", 1500, seed: 2),
            args => Run(SpecJournal, ["add", .. args.Split('|')]));

    [Fact]
    public void The_harness_names_the_input_that_threw()
    {
        var failure = Assert.Throws<Xunit.Sdk.XunitException>(() =>
            Fuzz.NeverThrows(["12", "twelve"], text => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Contains("Threw FormatException", failure.Message);
        Assert.Contains("twelve", failure.Message);
    }
}
