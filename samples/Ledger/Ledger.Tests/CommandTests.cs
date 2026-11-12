using Ledger.Core;
using static Ledger.Tests.Harness;

namespace Ledger.Tests;

/// <summary>L-10 to L-15: the commands, their options and exit codes.</summary>
public class CommandTests
{
    [Fact]
    public void L10_check_lists_problems_on_stderr_and_exits_1()
    {
        var outcome = Run("open 2026-01-01 Asset:Cash\n", "check");
        Assert.Equal("", outcome.Stdout);
        Assert.StartsWith("line 1: ", outcome.Stderr);
        Assert.Equal(1, outcome.ExitCode);
    }

    [Fact]
    public void L10_file_option_defaults_to_ledger_journal()
    {
        Assert.Equal("ledger.journal", CommandLine.Parse(["check"]).Match(i => i.File, e => e.Message));
        Assert.Equal("my.journal", CommandLine.Parse(["check", "--file", "my.journal"]).Match(i => i.File, e => e.Message));
        Assert.Equal("my.journal", CommandLine.Parse(["--file", "my.journal", "check"]).Match(i => i.File, e => e.Message));
    }

    [Fact]
    public void L10_missing_journal_is_an_error()
    {
        var outcome = Run(null, "check");
        Assert.Equal("error: journal 'ledger.journal' not found\n", outcome.Stderr);
        Assert.Equal(1, outcome.ExitCode);
    }

    [Fact]
    public void L11_balance_as_of_leaves_out_later_transactions()
    {
        var outcome = Run(SpecJournal, "balance", "--as-of", "2026-01-10");
        Assert.Equal(
            "            -82.40  Assets\n" +
            "            -82.40    Bank\n" +
            "            -82.40      Everyday\n" +
            "             82.40  Expenses\n" +
            "             82.40    Groceries\n" +
            "              0.00  Income\n" +
            "              0.00    Salary\n" +
            "--------------------\n" +
            "              0.00\n",
            outcome.Stdout);
    }

    [Fact]
    public void L11_balance_as_of_leaves_out_accounts_opened_later()
    {
        var outcome = Run(SpecJournal, "balance", "--as-of", "2025-12-31");
        Assert.Equal("--------------------\n              0.00\n", outcome.Stdout);
    }

    [Fact]
    public void L11_balance_with_prefix_shows_only_that_branch()
    {
        var outcome = Run(SpecJournal, "balance", "Income");
        Assert.Equal(
            "          -3200.00  Income\n" +
            "          -3200.00    Salary\n" +
            "--------------------\n" +
            "          -3200.00\n",
            outcome.Stdout);
    }

    [Fact]
    public void L11_prefix_matches_whole_segments()
    {
        var journal = With(
            "2026-01-02 Move\n  Assets:Bank  1.00\n  Assets:Banking  2.00\n  Equity:Opening\n",
            "Assets:Bank", "Assets:Banking", "Equity:Opening");
        Assert.Equal(
            "              1.00  Assets\n" +
            "              1.00    Bank\n" +
            "--------------------\n" +
            "              1.00\n",
            Run(journal, "balance", "Assets:Bank").Stdout);
    }

    [Fact]
    public void L11_accounts_sort_as_a_tree_by_name()
    {
        // Ordinal sorting of the full name would put "Bank2" between "Bank" and "Bank:Savings".
        var journal = With(
            "2026-01-02 Move\n  Assets:Bank:Savings  1.00\n  Assets:Bank2  2.00\n  Assets:Bank  3.00\n  Equity:Opening\n",
            "Assets:Bank2", "Assets:Bank:Savings", "Assets:Bank", "Equity:Opening");
        Assert.Equal(
            "              6.00  Assets\n" +
            "              4.00    Bank\n" +
            "              1.00      Savings\n" +
            "              2.00    Bank2\n" +
            "             -6.00  Equity\n" +
            "             -6.00    Opening\n" +
            "--------------------\n" +
            "              0.00\n",
            Run(journal, "balance").Stdout);
    }

    [Fact]
    public void L11_largest_amounts_fit_the_column()
    {
        var journal = With("2026-01-02 Win\n  Assets:Cash  999999999999.99\n  Income:Lottery\n", "Assets:Cash", "Income:Lottery");
        Assert.Equal(
            "   999999999999.99  Assets\n" +
            "   999999999999.99    Cash\n" +
            "  -999999999999.99  Income\n" +
            "  -999999999999.99    Lottery\n" +
            "--------------------\n" +
            "              0.00\n",
            Run(journal, "balance").Stdout);
    }

    [Fact]
    public void L11_balance_refuses_an_invalid_journal()
    {
        var outcome = Run(TypoJournal, "balance");
        Assert.Equal("", outcome.Stdout);
        Assert.Equal("line 14: transaction 'Typo' does not balance: off by 9.00\n", outcome.Stderr);
        Assert.Equal(1, outcome.ExitCode);
    }

    [Fact]
    public void L12_register_with_no_matches_prints_nothing()
    {
        var outcome = Run(SpecJournal, "register", "Liabilities");
        Assert.Equal("", outcome.Stdout);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public void L12_register_includes_every_matching_account()
    {
        var journal = With(
            "2026-01-02 Shop\n  Expenses:Groceries  20.00\n  Assets:Cash\n\n" +
            "2026-01-03 Lunch\n  Expenses:Eating  12.50\n  Assets:Cash\n",
            "Expenses:Groceries", "Expenses:Eating", "Assets:Cash");
        Assert.Equal(
            "2026-01-02  Shop   Expenses:Groceries     20.00     20.00\n" +
            "2026-01-03  Lunch  Expenses:Eating        12.50     32.50\n",
            Run(journal, "register", "Expenses").Stdout);
    }

    [Fact]
    public void L13_add_appends_a_valid_transaction()
    {
        var outcome = Run(SpecJournal, "add", "2026-02-02", "Weekly shop", "Expenses:Groceries", "45.10", "Assets:Bank:Everyday");
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal("", outcome.Stdout + outcome.Stderr);
        Assert.Equal("\n2026-02-02 Weekly shop\n  Expenses:Groceries  45.10\n  Assets:Bank:Everyday\n", outcome.Appended());

        var after = Run(SpecJournal + outcome.Appended(), "check");
        Assert.Equal("OK: 3 transactions, 3 accounts\n", after.Stdout);
    }

    [Fact]
    public void L13_add_accepts_all_amounts_given()
    {
        var outcome = Run(SpecJournal, "add", "2026-02-02", "Split", "Expenses:Groceries", "10", "Assets:Bank:Everyday", "-10");
        Assert.Equal("\n2026-02-02 Split\n  Expenses:Groceries  10\n  Assets:Bank:Everyday  -10\n", outcome.Appended());
    }

    [Fact]
    public void L13_add_refuses_an_unbalanced_transaction()
    {
        var outcome = Run(SpecJournal, "add", "2026-02-02", "Oops", "Expenses:Groceries", "10", "Assets:Bank:Everyday", "-9");
        Assert.Equal("error: transaction 'Oops' does not balance: off by 1.00\n", outcome.Stderr);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Null(outcome.Appended());
    }

    [Fact]
    public void L13_add_refuses_a_single_posting()
    {
        var outcome = Run(SpecJournal, "add", "2026-02-02", "Oops", "Expenses:Groceries", "10");
        Assert.Equal("error: transaction 'Oops' needs at least two postings\nerror: transaction 'Oops' does not balance: off by 10.00\n", outcome.Stderr);
        Assert.Null(outcome.Appended());
    }

    [Fact]
    public void L13_add_refuses_a_date_before_the_last_transaction()
    {
        var outcome = Run(SpecJournal, "add", "2026-01-10", "Late entry", "Expenses:Groceries", "1.00", "Assets:Bank:Everyday");
        Assert.Equal("error: transaction 'Late entry' on 2026-01-10 is out of order: it comes after a transaction on 2026-01-15\n", outcome.Stderr);
        Assert.Null(outcome.Appended());
    }

    [Fact]
    public void L13_add_refuses_a_bad_date_or_amount()
    {
        Assert.Equal(
            "error: '2026-02-31' is not a valid date (expected YYYY-MM-DD)\n",
            Run(SpecJournal, "add", "2026-02-31", "X", "Expenses:Groceries", "1.00", "Assets:Bank:Everyday").Stderr);
        Assert.Equal(
            "error: amount 1.234 has more than two decimal places\n",
            Run(SpecJournal, "add", "2026-02-02", "X", "Expenses:Groceries", "1.234", "Assets:Bank:Everyday").Stderr);
    }

    [Fact]
    public void L13_add_refuses_when_the_journal_is_already_invalid()
    {
        var outcome = Run(TypoJournal, "add", "2026-02-02", "Fine", "Expenses:Groceries", "1.00", "Assets:Bank:Everyday");
        Assert.Equal("line 14: transaction 'Typo' does not balance: off by 9.00\n", outcome.Stderr);
        Assert.Null(outcome.Appended());
    }

    [Fact]
    public void L14_open_appends_a_declaration()
    {
        var outcome = Run(SpecJournal, "open", "2026-02-01", "Expenses:Coffee");
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal("\nopen 2026-02-01 Expenses:Coffee\n", outcome.Appended());

        var adding = Run(SpecJournal + outcome.Appended(), "add", "2026-02-02", "Coffee", "Expenses:Coffee", "4.50", "Assets:Bank:Everyday");
        Assert.Equal("", adding.Stderr);
        Assert.Equal(0, adding.ExitCode);
    }

    [Fact]
    public void L14_open_follows_other_opens_directly()
    {
        Assert.Equal("open 2026-02-01 Expenses:Coffee\n", Run("open 2026-01-01 Assets:Cash\n", "open", "2026-02-01", "Expenses:Coffee").Appended());
        Assert.Equal("\nopen 2026-02-01 Expenses:Coffee\n", Run("open 2026-01-01 Assets:Cash", "open", "2026-02-01", "Expenses:Coffee").Appended());
        Assert.Equal("open 2026-02-01 Expenses:Coffee\n", Run(null, "open", "2026-02-01", "Expenses:Coffee").Appended());
    }

    [Fact]
    public void L14_opening_an_account_twice_is_an_error()
    {
        var outcome = Run(SpecJournal, "open", "2026-02-01", "Income:Salary");
        Assert.Equal("error: account 'Income:Salary' is already open (line 3)\n", outcome.Stderr);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Null(outcome.Appended());
    }

    [Fact]
    public void L14_open_checks_the_account_type()
    {
        var outcome = Run(SpecJournal, "open", "2026-02-01", "Savings:Jar");
        Assert.Equal("error: account 'Savings:Jar' has unknown type 'Savings' (expected Assets, Liabilities, Equity, Income, Expenses)\n", outcome.Stderr);
        Assert.Equal(1, outcome.ExitCode);
    }

    [Theory]
    [InlineData("error: no command given\n")]
    [InlineData("error: unknown command 'report'\n", "report")]
    [InlineData("error: wrong arguments for 'register'\n", "register")]
    [InlineData("error: wrong arguments for 'check'\n", "check", "extra")]
    [InlineData("error: wrong arguments for 'add'\n", "add", "2026-01-01", "Desc")]
    [InlineData("error: unknown option --verbose\n", "check", "--verbose")]
    [InlineData("error: --file needs a value\n", "check", "--file")]
    [InlineData("error: --as-of only applies to 'balance'\n", "check", "--as-of", "2026-01-01")]
    [InlineData("error: '2026-1-1' is not a valid date (expected YYYY-MM-DD)\n", "balance", "--as-of", "2026-1-1")]
    [InlineData("error: amount '5.00' must follow an account\n", "add", "2026-01-01", "Desc", "5.00", "Assets:Cash")]
    public void L15_usage_errors_exit_2_with_usage_on_stderr(string firstLine, params string[] args)
    {
        var outcome = Run(SpecJournal, args);
        Assert.Equal("", outcome.Stdout);
        Assert.Equal(firstLine + CommandLine.Usage, outcome.Stderr);
        Assert.Equal(2, outcome.ExitCode);
    }

    [Fact]
    public void L15_normal_output_goes_to_stdout_and_errors_to_stderr()
    {
        var ok = Run(SpecJournal, "check");
        var bad = Run(TypoJournal, "check");
        Assert.Equal(("OK: 2 transactions, 3 accounts\n", "", 0), (ok.Stdout, ok.Stderr, ok.ExitCode));
        Assert.Equal(("", "line 14: transaction 'Typo' does not balance: off by 9.00\n", 1), (bad.Stdout, bad.Stderr, bad.ExitCode));
    }
}
