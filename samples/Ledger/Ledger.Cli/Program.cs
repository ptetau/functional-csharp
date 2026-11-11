// The imperative shell: read the arguments and the journal, let the core decide, then do what it says.
using Functional;
using Ledger.Core;

var outcome = CommandLine.Parse(args).Match(
    invocation => App.Execute(invocation, File.Exists(invocation.File)
        ? Option.Some(File.ReadAllText(invocation.File))
        : Option.None<string>()),
    App.Usage);

var appended = outcome.Append.Match(
    append => { File.AppendAllText(append.Path, append.Text); return true; },
    () => false);

Console.Out.Write(outcome.Stdout);
Console.Error.Write(outcome.Stderr);
return outcome.ExitCode;
