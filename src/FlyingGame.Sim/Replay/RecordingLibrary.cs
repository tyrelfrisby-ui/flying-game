using System.IO;

namespace FlyingGame.Sim.Replay;

/// <summary>
/// The pilot's named recordings: one .fgrec file per session in a folder the game owns (Application.persistentDataPath/
/// Recordings on every platform; never StreamingAssets, which is read-only and a zip on Android). The file is named by
/// the session's stable <see cref="FlightSession.Id"/>, so renaming only rewrites the name inside it. Saves go to a
/// temporary file first and are moved into place, so a crash mid-save leaves the old recording, not half of the new one.
/// </summary>
public sealed class RecordingLibrary
{
    public const string Extension = ".fgrec";
    public readonly string Folder;

    public RecordingLibrary(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(folder);
    }

    /// <summary>Every readable recording, newest first. Unreadable files are skipped and reported in <paramref name="bad"/>.</summary>
    public List<RecordingSummary> List(List<string>? bad = null)
    {
        var list = new List<RecordingSummary>();
        foreach (string path in Directory.GetFiles(Folder, "*" + Extension))
        {
            try
            {
                using FileStream fs = File.OpenRead(path);
                list.Add(RecordingCodec.ReadSummary(fs));
            }
            catch (System.Exception e) when (e is IOException or InvalidDataException or System.UnauthorizedAccessException)
            {
                bad?.Add(Path.GetFileName(path));
            }
        }
        list.Sort((a, b) => b.CreatedUtcTicks.CompareTo(a.CreatedUtcTicks));
        return list;
    }

    public bool Exists(string id) => IsValidId(id) && File.Exists(PathFor(id));

    public void Save(FlightSession session)
    {
        string path = PathFor(session.Id), tmp = path + ".tmp";
        using (FileStream fs = File.Create(tmp)) RecordingCodec.Write(fs, session);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    public FlightSession Load(string id)
    {
        using FileStream fs = File.OpenRead(PathFor(id));
        return RecordingCodec.Read(fs);
    }

    /// <summary>Change a recording's display name. The name is trimmed; an empty name is refused.</summary>
    public bool Rename(string id, string newName)
    {
        newName = (newName ?? "").Trim();
        if (newName.Length == 0 || !Exists(id)) return false;
        FlightSession s = Load(id);
        s.Name = newName;
        Save(s);
        return true;
    }

    public bool Delete(string id)
    {
        if (!Exists(id)) return false;
        File.Delete(PathFor(id));
        return true;
    }

    /// <summary>"Recording 1", "Recording 2", … — the first <paramref name="stem"/> n not already in the list.</summary>
    public string NextName(string stem = "Recording")
    {
        var taken = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (RecordingSummary s in List()) taken.Add(s.Name);
        for (int n = 1; ; n++) { string name = $"{stem} {n}"; if (!taken.Contains(name)) return name; }
    }

    /// <summary>Ids become file names, so only letters, digits, '-' and '_' are accepted (no path in a name, ever).</summary>
    public static bool IsValidId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
        foreach (char c in id) if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')) return false;
        return true;
    }

    private string PathFor(string id)
    {
        if (!IsValidId(id)) throw new System.ArgumentException($"bad recording id '{id}'");
        return Path.Combine(Folder, id + Extension);
    }
}
