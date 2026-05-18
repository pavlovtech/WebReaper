namespace WebReaper.DataAccess;

/// <summary>
/// The one home (ADR-0011, CONTEXT.md "File persistence prep") for the three
/// things every file-backed adapter must get right <em>before</em> it writes:
/// eager, unconditional directory creation; a deterministic
/// <c>DataCleanupOnStart</c> applied at start (not lazily on first write); and
/// a missing file read as empty rather than throwing.
///
/// A small <em>stateless</em> helper — deliberately not a held-handle
/// durability layer and not a shared lock (the rejected substrate, see
/// ADR-0011). Each adapter keeps its own write/read essence (whole-file
/// replace, the resumable cursor, the in-memory mirror, the buffered drain)
/// and only delegates this prep, so the three single-copy bugs the four
/// adapters had drifted into become unrepresentable.
/// </summary>
internal static class FilePersistencePrep
{
    /// <summary>
    /// Ensure the directory containing <paramref name="path"/> exists. Eager,
    /// unconditional and idempotent (a no-op if it already exists). Safe for a
    /// bare filename with no directory part. Call before any write so a key /
    /// file in a not-yet-existing directory does not throw.
    /// </summary>
    public static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Deterministic cleanup-on-start: when <paramref name="dataCleanupOnStart"/>
    /// is <c>true</c>, delete <paramref name="path"/> now — at start, before
    /// any write — so a run that produces zero rows still clears stale state.
    /// A no-op when the flag is <c>false</c> or the file is absent.
    /// </summary>
    public static void CleanupOnStart(string path, bool dataCleanupOnStart)
    {
        if (dataCleanupOnStart && File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// Whole-file read; an absent file reads as <c>null</c> rather than
    /// throwing. The role above decides what absence <em>means</em> (the
    /// keyed-blob seam maps it to "key absent"). UTF-8, no BOM — the
    /// <see cref="File"/> default, unchanged.
    /// </summary>
    public static async Task<string?> ReadAllTextOrNullAsync(string path)
        => File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
}
