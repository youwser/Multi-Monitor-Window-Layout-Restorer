using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiMonitorRestorer.Core;

/// <summary>
/// Handles all JSON file I/O for layout profiles.
/// Profiles are stored in <c>%AppData%\WindowLayoutRestorer\profiles\</c>.
/// Each file is named <c>{signatureHash}_{sanitizedName}.json</c>.
/// </summary>
internal sealed class ProfileStorage
{
    // ─── Fields ──────────────────────────────────────────────────────────────

    private readonly string _profilesDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented            = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition   = JsonIgnoreCondition.WhenWritingNull,
    };

    // ─── Constructor ─────────────────────────────────────────────────────────

    public ProfileStorage(string? storagePath = null)
    {
        _profilesDir = storagePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WindowLayoutRestorer",
                "profiles");

        Directory.CreateDirectory(_profilesDir);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Persists <paramref name="profile"/> to disk, overwriting any existing
    /// file with the same monitor signature hash + profile name combination.
    /// </summary>
    public void Save(LayoutProfile profile)
    {
        string path = BuildFilePath(profile.MonitorSignature, profile.ProfileName);
        string json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads the best-matching profile for <paramref name="monitorSignature"/>:
    /// <list type="number">
    ///   <item>Exact match on full monitor signature (resolution + position).</item>
    ///   <item>Fallback: match on position-only signature (DPI-agnostic) when
    ///         the user changed DPI scaling on one screen between sessions.</item>
    /// </list>
    /// Returns <c>null</c> if no match exists.
    /// </summary>
    public LayoutProfile? Load(string monitorSignature, string? positionSignature = null)
    {
        LayoutProfile? fallback = null;

        foreach (string file in Directory.EnumerateFiles(_profilesDir, "*.json"))
        {
            try
            {
                var profile = DeserializeFile(file);
                if (profile is null) continue;

                // Exact match — best result, return immediately
                if (string.Equals(profile.MonitorSignature, monitorSignature, StringComparison.Ordinal))
                    return profile;

                // Position-only fallback — keep the first candidate
                if (fallback is null && positionSignature is not null &&
                    string.Equals(profile.PositionSignature, positionSignature, StringComparison.Ordinal))
                {
                    fallback = profile;
                }
            }
            catch { /* corrupted file — skip */ }
        }

        return fallback; // null if nothing matched at all
    }

    /// <summary>
    /// Returns all valid profiles found in the storage directory.
    /// </summary>
    public IList<LayoutProfile> LoadAll()
    {
        var results = new List<LayoutProfile>();
        foreach (string file in Directory.EnumerateFiles(_profilesDir, "*.json"))
        {
            try
            {
                var profile = DeserializeFile(file);
                if (profile is not null)
                    results.Add(profile);
            }
            catch { /* corrupted file — skip */ }
        }
        return results;
    }

    /// <summary>
    /// Deletes all profiles matching <paramref name="monitorSignature"/>.
    /// </summary>
    public void Delete(string monitorSignature)
    {
        foreach (string file in Directory.EnumerateFiles(_profilesDir, "*.json"))
        {
            try
            {
                var profile = DeserializeFile(file);
                if (profile is not null &&
                    string.Equals(profile.MonitorSignature, monitorSignature, StringComparison.Ordinal))
                {
                    File.Delete(file);
                }
            }
            catch { /* ignore */ }
        }
    }

    /// <summary>Deletes a profile by its exact file path.</summary>
    public void DeleteByPath(string filePath)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    /// <summary>Returns the full path to the profiles directory.</summary>
    public string ProfilesDirectory => _profilesDir;

    // ─── Private Helpers ─────────────────────────────────────────────────────

    private string BuildFilePath(string signature, string profileName)
    {
        string hash      = DisplayMonitor.HashSignature(signature);
        string safeName  = SanitizeFilename(profileName);
        return Path.Combine(_profilesDir, $"{hash}_{safeName}.json");
    }

    private static string SanitizeFilename(string name)
    {
        // Replace illegal filename chars with underscores and trim
        char[] invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        safe = safe.Trim().Replace(' ', '-');
        if (safe.Length > 40) safe = safe[..40];
        return safe.Length > 0 ? safe : "layout";
    }

    private static LayoutProfile? DeserializeFile(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LayoutProfile>(json, JsonOptions);
    }
}
