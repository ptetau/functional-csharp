using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using CharacterReviews.Core;
using Functional;

namespace CharacterReviews.Web;

/// <summary>
/// Holds the current State and keeps it in a JSON file (C-14). Every change goes through
/// <see cref="Apply"/>, which runs one pure core function under a lock and writes the result to disk
/// before anyone else can see it.
/// </summary>
public sealed class Store
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly Lock _gate = new();
    private State _state;

    public Store(string path)
    {
        _path = Path.GetFullPath(path);
        _state = Load(_path);
    }

    public State Current
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>Run a core transition against the latest state, and save the new state if it made one.</summary>
    public Response Apply(Func<State, Outcome> change)
    {
        lock (_gate)
        {
            var outcome = change(_state);
            outcome.Saved.IfSome(saved =>
            {
                Save(_path, saved);
                _state = saved;
            });
            return outcome.Response;
        }
    }

    private static State Load(string path)
    {
        if (!File.Exists(path)) return State.Empty;
        // A list missing from the file comes back as a default ValueList, which is empty, so no clean-up is needed.
        return JsonSerializer.Deserialize<State>(File.ReadAllText(path), Json) ?? State.Empty;
    }

    /// <summary>Write to a temporary file and move it into place, so a crash never leaves half a file.</summary>
    private static void Save(string path, State state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, path, overwrite: true);
    }
}
