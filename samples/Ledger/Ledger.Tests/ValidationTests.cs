using static Ledger.Tests.Harness;

namespace Ledger.Tests;

/// <summary>L-1 to L-9: each rule `ledger check` enforces, with the exact message.</summary>
public class ValidationTests
{
    private static string Check(string journal)
    {
        var outcome = Run(journal, "check");
        Assert.Equal(outcome.Stderr == "" ? 0 : 1, outcome.ExitCode);
        return outcome.Stderr == "" ? outcome.Stdout : outcome.Stderr;
    }

    [Fact]
    public void L1_open_needs_a_date_and_an_account()
    {
        Assert.Equal("line 1: expected 'open <date> <account>'\n", Check("open 2026-01-01\n"));
    }

    [Fact]
    public void L1_dates_are_yyyy_mm_dd()
    {
        Assert.Equal("line 1: '2026-1-01' is not a valid date (expected YYYY-MM-DD)\n", Check("open 2026-1-01 Assets:Cash\n"));
        Assert.Equal("line 1: '2026-02-30' is not a valid date (expected YYYY-MM-DD)\n", Check("open 2026-02-30 Assets:Cash\n"));
    }

    [Fact]
    public void L1_open_cannot_have_postings()
    {
        Assert.Equal("line 2: an 'open' line can't have postings under it\n", Check("open 2026-01-01 Assets:Cash\n  Assets:Cash  1.00\n"));
    }

    [Fact]
    public void L2_transaction_header_needs_a_valid_date()
    {
        var journal = With("2026-13-01 Bad\n  Assets:Cash  1.00\n  Equity:Opening\n", "Assets:Cash", "Equity:Opening");
        Assert.Equal("line 4: '2026-13-01' is not a valid date (expected YYYY-MM-DD)\n", Check(journal));
    }

    [Fact]
    public void L2_transaction_needs_a_description()
    {
        var journal = With("2026-01-02\n  Assets:Cash  1.00\n  Equity:Opening\n", "Assets:Cash", "Equity:Opening");
        Assert.Equal("line 4: transaction on 2026-01-02 has no description\n", Check(journal));
    }

    [Fact]
    public void L2_postings_must_be_inside_a_transaction()
    {
        Assert.Equal("line 1: posting is not inside a transaction\n", Check("  Assets:Cash  1.00\n"));
    }

    [Fact]
    public void L2_posting_is_an_account_then_an_optional_amount()
    {
        var journal = With("2026-01-02 Bad\n  Assets:Cash  1.00 extra\n  Equity:Opening  ten\n", "Assets:Cash", "Equity:Opening");
        Assert.Equal(
            "line 5: expected '<account>  <amount>' or just '<account>'\n" +
            "line 6: 'ten' is not a valid amount\n",
            Check(journal));
    }

    [Fact]
    public void L2_comments_and_blank_lines_are_ignored()
    {
        var journal = "; header\n\n   \nopen 2026-01-01 Assets:Cash\n  ; an indented comment\nopen 2026-01-01 Equity:Opening\n";
        Assert.Equal("OK: 0 transactions, 2 accounts\n", Check(journal));
    }

    [Fact]
    public void L3_one_posting_may_leave_out_its_amount()
    {
        var journal = With("2026-01-02 Start\n  Assets:Cash  25.00\n  Equity:Opening\n", "Assets:Cash", "Equity:Opening");
        Assert.Equal("OK: 1 transactions, 2 accounts\n", Check(journal));
        Assert.Contains("-25.00  Equity", Run(journal, "balance").Stdout);
    }

    [Fact]
    public void L3_at_most_one_posting_may_leave_out_its_amount()
    {
        var journal = With("2026-01-02 Start\n  Assets:Cash  25.00\n  Equity:Opening\n  Equity:Other\n", "Assets:Cash", "Equity:Opening", "Equity:Other");
        Assert.Equal("line 5: transaction 'Start' has 2 postings without an amount (at most one is allowed)\n", Check(journal));
    }

    [Fact]
    public void L4_transaction_needs_two_postings()
    {
        var journal = With("2026-01-02 Lonely\n  Assets:Cash  0.00\n", "Assets:Cash");
        Assert.Equal("line 3: transaction 'Lonely' needs at least two postings\n", Check(journal));
    }

    [Fact]
    public void L5_unbalanced_amount_keeps_its_sign()
    {
        var journal = With("2026-01-02 Short\n  Assets:Cash  1.00\n  Equity:Opening  -3.50\n", "Assets:Cash", "Equity:Opening");
        Assert.Equal("line 4: transaction 'Short' does not balance: off by -2.50\n", Check(journal));
    }

    [Fact]
    public void L6_posting_account_must_be_open()
    {
        var journal = With("2026-01-02 Gift\n  Assets:Cash  5.00\n  Income:Gifts\n", "Assets:Cash");
        Assert.Equal("line 5: account 'Income:Gifts' is not open on 2026-01-02\n", Check(journal));
    }

    [Fact]
    public void L6_posting_account_must_be_open_on_or_before_the_date()
    {
        var journal =
            "open 2026-01-01 Assets:Cash\nopen 2026-01-02 Income:Gifts\n\n" +
            "2026-01-01 Too early\n  Assets:Cash  5.00\n  Income:Gifts\n\n" +
            "2026-01-02 Same day\n  Assets:Cash  5.00\n  Income:Gifts\n";
        Assert.Equal("line 6: account 'Income:Gifts' is not open on 2026-01-01\n", Check(journal));
    }

    [Fact]
    public void L7_account_type_must_be_one_of_the_five()
    {
        Assert.Equal(
            "line 1: account 'Asset:Cash' has unknown type 'Asset' (expected Assets, Liabilities, Equity, Income, Expenses)\n",
            Check("open 2026-01-01 Asset:Cash\n"));
    }

    [Fact]
    public void L7_account_names_have_no_empty_parts()
    {
        Assert.Equal("line 1: account 'Assets::Cash' has an empty part\n", Check("open 2026-01-01 Assets::Cash\n"));
    }

    [Fact]
    public void L7_all_five_types_are_accepted()
    {
        var journal = "open 2026-01-01 Assets:A\nopen 2026-01-01 Liabilities:B\nopen 2026-01-01 Equity:C\nopen 2026-01-01 Income:D\nopen 2026-01-01 Expenses:E\n";
        Assert.Equal("OK: 0 transactions, 5 accounts\n", Check(journal));
    }

    [Fact]
    public void L8_amounts_have_at_most_two_decimal_places()
    {
        var journal = With("2026-01-02 Fuel\n  Expenses:Car  1.005\n  Assets:Cash  -1.005\n", "Expenses:Car", "Assets:Cash");
        Assert.Equal(
            "line 5: amount 1.005 has more than two decimal places\n" +
            "line 6: amount -1.005 has more than two decimal places\n",
            Check(journal));
    }

    [Fact]
    public void L8_totals_are_exact()
    {
        // 0.1 + 0.2 - 0.3 is not 0 in floating point.
        var journal = With("2026-01-02 Split\n  Expenses:A  0.10\n  Expenses:B  0.20\n  Assets:Cash  -0.30\n", "Expenses:A", "Expenses:B", "Assets:Cash");
        Assert.Equal("OK: 1 transactions, 3 accounts\n", Check(journal));
    }

    [Fact]
    public void L8_huge_amounts_are_rejected_instead_of_overflowing()
    {
        var journal = With("2026-01-02 Big\n  Assets:Cash  79228162514264337593543950335\n  Equity:Opening  79228162514264337593543950335\n", "Assets:Cash", "Equity:Opening");
        Assert.Equal(
            "line 5: amount 79228162514264337593543950335 is too large\n" +
            "line 6: amount 79228162514264337593543950335 is too large\n",
            Check(journal));
    }

    [Fact]
    public void L9_transactions_must_be_in_date_order()
    {
        var journal = With(
            "2026-01-05 Later\n  Assets:Cash  1.00\n  Equity:Opening\n\n" +
            "2026-01-03 Earlier\n  Assets:Cash  1.00\n  Equity:Opening\n\n" +
            "2026-01-04 Also earlier\n  Assets:Cash  1.00\n  Equity:Opening\n",
            "Assets:Cash", "Equity:Opening");
        Assert.Equal(
            "line 8: transaction 'Earlier' on 2026-01-03 is out of order: it comes after a transaction on 2026-01-05\n" +
            "line 12: transaction 'Also earlier' on 2026-01-04 is out of order: it comes after a transaction on 2026-01-05\n",
            Check(journal));
    }

    [Fact]
    public void L9_same_day_transactions_keep_file_order()
    {
        var journal = With(
            "2026-01-05 Zebra\n  Assets:Cash  1.00\n  Equity:Opening\n\n" +
            "2026-01-05 Apple\n  Assets:Cash  2.00\n  Equity:Opening\n",
            "Assets:Cash", "Equity:Opening");
        Assert.Equal(
            "2026-01-05  Zebra  Assets:Cash      1.00      1.00\n" +
            "2026-01-05  Apple  Assets:Cash      2.00      3.00\n",
            Run(journal, "register", "Assets").Stdout);
    }

    [Fact]
    public void L14_journal_cannot_open_an_account_twice()
    {
        Assert.Equal("line 2: account 'Assets:Cash' is already open (line 1)\n", Check("open 2026-01-01 Assets:Cash\nopen 2026-02-01 Assets:Cash\n"));
    }

    [Fact]
    public void Every_problem_is_reported_in_line_order()
    {
        var journal =
            "open 2026-01-01 Assets:Cash\n" +           // 1
            "open 2026-01-01 Bogus:Thing\n" +           // 2
            "\n" +                                      // 3
            "2026-01-02 One\n" +                        // 4
            "  Assets:Cash  1.00\n" +                   // 5
            "  Expenses:Missing  -2.00\n" +             // 6
            "\n" +                                      // 7
            "2026-01-01 Two\n" +                        // 8
            "  Assets:Cash  x\n" +                      // 9
            "  Assets:Cash\n";                          // 10
        Assert.Equal(
            "line 2: account 'Bogus:Thing' has unknown type 'Bogus' (expected Assets, Liabilities, Equity, Income, Expenses)\n" +
            "line 4: transaction 'One' does not balance: off by -1.00\n" +
            "line 6: account 'Expenses:Missing' is not open on 2026-01-02\n" +
            "line 9: 'x' is not a valid amount\n",
            Check(journal));
    }

    [Fact]
    public void Journal_without_trailing_newline_or_with_crlf_is_read()
    {
        Assert.Equal("OK: 2 transactions, 3 accounts\n", Check(SpecJournal.TrimEnd('\n')));
        Assert.Equal("OK: 2 transactions, 3 accounts\n", Check(SpecJournal.Replace("\n", "\r\n")));
    }

    [Fact]
    public void Empty_journal_is_valid()
    {
        Assert.Equal("OK: 0 transactions, 0 accounts\n", Check(""));
    }
}
