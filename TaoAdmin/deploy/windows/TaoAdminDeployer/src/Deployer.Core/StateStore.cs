// StateStore: resumable install checkpoint. One step id per line in the state file.
namespace TaoAdmin.Deployer.Core;

public sealed class StateStore
{
    private readonly string _path;
    private readonly object _lock = new();

    public StateStore(string path) => _path = path;

    public HashSet<string> Load()
    {
        lock (_lock)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(_path)) return set;
            foreach (var line in File.ReadAllLines(_path))
            {
                var t = line.Trim();
                if (t.Length > 0) set.Add(t);
            }
            return set;
        }
    }

    public void MarkDone(string stepId)
    {
        lock (_lock)
        {
            if (!File.Exists(_path)) return; // state file created by EnsureStateFile
            var cur = File.ReadAllLines(_path);
            if (cur.Any(l => l.Trim() == stepId)) return;
            File.AppendAllText(_path, stepId + Environment.NewLine);
        }
    }

    public void EnsureStateFile()
    {
        lock (_lock)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (!File.Exists(_path)) File.WriteAllText(_path, string.Empty);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
    }
}
