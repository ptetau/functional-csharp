using System.Diagnostics;
using static Ledger.Tests.Harness;

namespace Ledger.Tests;

/// <summary>Runs the real `ledger` executable on a journal file, in a folder of its own.</summary>
public sealed class CliTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ledger-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private (string Stdout, string Stderr, int ExitCode) Ledger(params string[] args)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "ledger.dll");
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = new ProcessStartInfo(host) { WorkingDirectory = _folder, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll);
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (stdout.Result, stderr.Result, process.ExitCode);
    }

    private string JournalPath => Path.Combine(_folder, "ledger.journal");

    [Fact]
    public void Spec_examples_match_when_run_for_real()
    {
        File.WriteAllText(JournalPath, SpecJournal);

        Assert.Equal(("OK: 2 transactions, 3 accounts\n", "", 0), Ledger("check"));

        Assert.Equal((
            "           3117.60  Assets\n" +
            "           3117.60    Bank\n" +
            "           3117.60      Everyday\n" +
            "             82.40  Expenses\n" +
            "             82.40    Groceries\n" +
            "          -3200.00  Income\n" +
            "          -3200.00    Salary\n" +
            "--------------------\n" +
            "              0.00\n", "", 0),
            Ledger("balance"));

        Assert.Equal((
            "2026-01-05  Countdown  Assets:Bank:Everyday    -82.40    -82.40\n" +
            "2026-01-15  Pay        Assets:Bank:Everyday   3200.00   3117.60\n", "", 0),
            Ledger("register", "Assets:Bank"));

        Assert.Equal(("", "error: account 'Expenses:Coffee' is not open on 2026-02-02\n", 1),
            Ledger("add", "2026-02-02", "Coffee", "Expenses:Coffee", "4.50", "Assets:Bank:Everyday"));
        Assert.Equal(SpecJournal, File.ReadAllText(JournalPath));

        File.WriteAllText(Path.Combine(_folder, "typo.journal"), TypoJournal);
        Assert.Equal(("", "line 14: transaction 'Typo' does not balance: off by 9.00\n", 1),
            Ledger("check", "--file", "typo.journal"));
    }

    [Fact]
    public void Open_then_add_appends_to_the_file()
    {
        File.WriteAllText(JournalPath, SpecJournal);

        Assert.Equal(("", "", 0), Ledger("open", "2026-02-01", "Expenses:Coffee"));
        Assert.Equal(("", "", 0), Ledger("add", "2026-02-02", "Coffee", "Expenses:Coffee", "4.50", "Assets:Bank:Everyday"));

        Assert.Equal(
            SpecJournal + "\nopen 2026-02-01 Expenses:Coffee\n\n2026-02-02 Coffee\n  Expenses:Coffee  4.50\n  Assets:Bank:Everyday\n",
            File.ReadAllText(JournalPath));
        Assert.Equal(("OK: 3 transactions, 4 accounts\n", "", 0), Ledger("check"));
    }

    [Fact]
    public void Usage_error_exits_2()
    {
        var (stdout, stderr, exitCode) = Ledger("frobnicate");
        Assert.Equal("", stdout);
        Assert.StartsWith("error: unknown command 'frobnicate'\nusage: ledger", stderr);
        Assert.Equal(2, exitCode);
    }
}
