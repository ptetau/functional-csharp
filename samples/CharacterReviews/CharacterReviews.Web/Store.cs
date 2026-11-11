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
            // Option has no side-effecting "if some" in the shared library, so test the case directly.
            if (outcome.Saved is Option<State>.Some { Value: var saved })
            {
                Save(_path, saved);
                _state = saved;
            }
            return outcome.Response;
        }
    }

    private static State Load(string path)
    {
        if (!File.Exists(path)) return State.Empty;
        var loaded = JsonSerializer.Deserialize<State>(File.ReadAllText(path), Json);
        return loaded is null
            ? State.Empty
            // Arrays missing from the file deserialize as default ImmutableArrays, which throw when used.
            : new State(
                loaded.Titles.IsDefault ? [] : loaded.Titles,
                loaded.Characters.IsDefault ? [] : loaded.Characters,
                loaded.Reviews.IsDefault ? [] : loaded.Reviews);
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
