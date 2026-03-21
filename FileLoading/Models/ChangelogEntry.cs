namespace FileLoading.Models;

/// <summary>
/// Represents a single changelog entry for a version release.
/// </summary>
public class ChangelogEntry
{
    /// <summary>
    /// The version number (e.g., "4.0.0").
    /// </summary>
    public required string Version { get; set; }

    /// <summary>
    /// The release date in ISO 8601 format (e.g., "2026-03-21").
    /// </summary>
    public required string ReleaseDate { get; set; }

    /// <summary>
    /// List of changes included in this version.
    /// </summary>
    public required List<string> Changes { get; set; }
}
