namespace DwgTrueView.App;

/// <summary>
/// Persists recently opened DWG/DXF paths for the Opened Recently command.
/// </summary>
internal sealed class RecentFileList
{
    public const int MaximumEntries = 12;
    private readonly List<string> _paths = [];
    private readonly string _storePath;

    public RecentFileList()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ProductInfo.Name);
        Directory.CreateDirectory(folder);
        _storePath = Path.Combine(folder, "recent.txt");
        Load();
    }

    public IReadOnlyList<string> Paths => _paths;

    public void Remember(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string full = Path.GetFullPath(path);
        _paths.RemoveAll(entry => entry.Equals(full, StringComparison.OrdinalIgnoreCase));
        _paths.Insert(0, full);
        if (_paths.Count > MaximumEntries)
        {
            _paths.RemoveRange(MaximumEntries, _paths.Count - MaximumEntries);
        }
        Save();
    }

    public void Remove(string path)
    {
        _paths.RemoveAll(entry => entry.Equals(path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_storePath))
        {
            return;
        }
        foreach (string line in File.ReadAllLines(_storePath))
        {
            string path = line.Trim();
            if (path.Length > 0
                && !_paths.Exists(entry => entry.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                _paths.Add(path);
            }
        }
    }

    private void Save()
    {
        File.WriteAllLines(_storePath, _paths);
    }
}
