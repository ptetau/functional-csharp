// The imperative shell: read the arguments and the reel file, let the core decide everything,
// then write the reel file (if it changed) and the output, and set the exit code.
using System.Text;
using Functional;
using HighlightReel.Core;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

try
{
    return App.ParseArgs(args)
        .Bind(invocation => App
            .Execute(invocation, File.Exists(invocation.ReelPath) ? Option.Some(File.ReadAllText(invocation.ReelPath)) : Option.None<string>())
            .Map(response => (invocation.ReelPath, response)))
        .Match(
            ok =>
            {
                ok.response.NewReelText.Match(
                    text => { File.WriteAllText(ok.ReelPath, text); return true; },
                    () => false);
                Console.Out.Write(ok.response.Output);
                return 0;
            },
            error =>
            {
                Console.Error.WriteLine($"error: {error.Message}");
                return 1;
            });
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
