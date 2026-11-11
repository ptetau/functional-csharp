# Ledger: double-entry personal finance on the command line

A command line tool that keeps personal accounts in a plain-text journal file, using double-entry bookkeeping.

## Concepts

- **Account**: a named place money sits or flows through. Names are colon-separated paths, e.g. `Assets:Bank:Everyday`. The first segment is the account's **type**, one of `Assets`, `Liabilities`, `Equity`, `Income`, `Expenses`.
- **Posting**: an amount moved into (positive) or out of (negative) one account.
- **Transaction**: a date, a description and two or more postings that **sum to exactly zero**.
- **Amount**: a decimal number with at most 2 decimal places, in one currency. There is one currency per journal (no symbol is stored).
- **Journal**: a text file holding account declarations and transactions in date order.

## Journal file format

```
; Lines starting with ';' are comments. Blank lines are ignored.
open 2026-01-01 Assets:Bank:Everyday
open 2026-01-01 Income:Salary
open 2026-01-01 Expenses:Groceries

2026-01-05 Countdown
  Expenses:Groceries     82.40
  Assets:Bank:Everyday

2026-01-15 Pay
  Assets:Bank:Everyday   3200.00
  Income:Salary         -3200.00
```

- L-1 `open <date> <account>` declares an account. Dates are `YYYY-MM-DD`.
- L-2 A transaction starts with `<date> <description>` at the start of a line. Its postings follow on lines indented by at least one space: `<account>` then, after at least two spaces, an optional amount.
- L-3 At most one posting in a transaction may leave its amount out. Its amount is whatever makes the transaction sum to zero.

## Validation

When reading a journal, the tool must report every problem it finds, each with the line number, and not just the first one.

- L-4 A transaction must have at least two postings.
- L-5 A transaction's postings must sum to zero. The error gives the amount it's out by.
- L-6 Every posting's account must be opened, on or before the transaction's date.
- L-7 An account's type must be one of the five types.
- L-8 Amounts have at most two decimal places. Totals must be exact, with no floating-point rounding.
- L-9 Transactions must be in date order. Transactions on the same day keep their file order.

## Commands

Every command takes the journal path with `--file <path>` (default: `ledger.journal` in the current folder).

- L-10 `ledger check`: validates the journal. Prints `OK: <n> transactions, <m> accounts` and exits 0, or lists the problems and exits 1.
- L-11 `ledger balance [--as-of <date>] [<account-prefix>]`: prints each account's balance and, for every parent path, the total of its children. Accounts are sorted by name. Amounts are right-aligned with two decimals. The last line is the grand total, which is 0.00 for a valid journal.
- L-12 `ledger register <account-prefix>`: lists every posting to matching accounts in date order, with a running total.
- L-13 `ledger add <date> <description> <account> <amount> <account> [<amount>]...`: appends a transaction. It must validate the transaction against the journal first and refuse (exit 1, journal unchanged) if it's invalid.
- L-14 `ledger open <date> <account>`: appends an account declaration. Opening an account twice is an error.
- L-15 Errors go to standard error. Normal output goes to standard output. Exit code 0 means success, 1 means invalid input or journal, and 2 means usage error.

## Examples

With the journal shown above:

```
$ ledger check
OK: 2 transactions, 3 accounts

$ ledger balance
           3117.60  Assets
           3117.60    Bank
           3117.60      Everyday
             82.40  Expenses
             82.40    Groceries
          -3200.00  Income
          -3200.00    Salary
--------------------
              0.00

$ ledger register Assets:Bank
2026-01-05  Countdown  Assets:Bank:Everyday    -82.40    -82.40
2026-01-15  Pay        Assets:Bank:Everyday   3200.00   3117.60
```

A transaction that doesn't balance, appended after a blank line at the end of the journal above:

```
2026-02-01 Typo
  Expenses:Groceries   10.00
  Assets:Bank:Everyday -1.00
```

```
$ ledger check
line 14: transaction 'Typo' does not balance: off by 9.00
```

Adding a transaction for an account that isn't open:

```
$ ledger add 2026-02-02 Coffee Expenses:Coffee 4.50 Assets:Bank:Everyday
error: account 'Expenses:Coffee' is not open on 2026-02-02
```

## Out of scope

Multiple currencies, prices, budgets, reconciliation, importing bank files, editing or deleting existing entries.
