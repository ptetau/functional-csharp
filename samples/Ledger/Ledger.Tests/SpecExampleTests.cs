using static Ledger.Tests.Harness;

namespace Ledger.Tests;

/// <summary>Every example in specs/ledger.md, with its exact output.</summary>
public class SpecExampleTests
{
    [Fact]
    public void L10_check_prints_ok_with_counts()
    {
        var outcome = Run(SpecJournal, "check");

        Assert.Equal("OK: 2 transactions, 3 accounts\n", outcome.Stdout);
        Assert.Equal("", outcome.Stderr);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public void L11_balance_prints_tree_and_total()
    {
        var outcome = Run(SpecJournal, "balance");

        Assert.Equal(
            "           3117.60  Assets\n" +
            "           3117.60    Bank\n" +
            "           3117.60      Everyday\n" +
            "             82.40  Expenses\n" +
            "             82.40    Groceries\n" +
            "          -3200.00  Income\n" +
            "          -3200.00    Salary\n" +
            "--------------------\n" +
            "              0.00\n",
            outcome.Stdout);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public void L12_register_lists_postings_with_running_total()
    {
        var outcome = Run(SpecJournal, "register", "Assets:Bank");

        Assert.Equal(
            "2026-01-05  Countdown  Assets:Bank:Everyday    -82.40    -82.40\n" +
            "2026-01-15  Pay        Assets:Bank:Everyday   3200.00   3117.60\n",
            outcome.Stdout);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public void L5_check_reports_unbalanced_transaction_with_line_and_difference()
    {
        var outcome = Run(TypoJournal, "check");

        Assert.Equal("", outcome.Stdout);
        Assert.Equal("line 14: transaction 'Typo' does not balance: off by 9.00\n", outcome.Stderr);
        Assert.Equal(1, outcome.ExitCode);
    }

    [Fact]
    public void L13_add_refuses_account_that_is_not_open()
    {
        var outcome = Run(SpecJournal, "add", "2026-02-02", "Coffee", "Expenses:Coffee", "4.50", "Assets:Bank:Everyday");

        Assert.Equal("", outcome.Stdout);
        Assert.Equal("error: account 'Expenses:Coffee' is not open on 2026-02-02\n", outcome.Stderr);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Null(outcome.Appended());
    }
}
