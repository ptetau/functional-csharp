using Functional;

namespace Ledger.Core;

/// <summary>Something wrong with the journal, found on a given line (1-based).</summary>
public sealed record Problem(int Line, string Message);

/// <summary>`open &lt;date&gt; &lt;account&gt;` as written in the journal.</summary>
public sealed record Opening(int Line, DateOnly Date, string Account);

/// <summary>A posting as written: the amount may be left out (L-3).</summary>
public sealed record Posting(int Line, string Account, Option<decimal> Amount);

/// <summary>A transaction as written, before it has been checked.</summary>
public sealed record Transaction(int Line, DateOnly Date, string Description, ValueList<Posting> Postings);

/// <summary>Everything that could be read from a journal file, in file order.</summary>
public sealed record Journal(ValueList<Opening> Openings, ValueList<Transaction> Transactions)
{
    public static readonly Journal Empty = new([], []);
}

/// <summary>One posting of a valid transaction, with its amount filled in.</summary>
public sealed record Entry(DateOnly Date, string Description, string Account, decimal Amount);

/// <summary>A journal that passed every check: the opening of each account and its postings in date order.</summary>
public sealed record Book(ValueList<Opening> Accounts, ValueList<Entry> Entries, int TransactionCount);
