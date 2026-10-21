using System;
using System.Collections.Generic;
using System.IO;

namespace PaperTodo;

/// <summary>
/// Resolves the directory that owns user data (state, its backup and the image LMDB file).
/// Development builds keep the historical portable layout beside the executable; published
/// single-file builds use a per-user directory so data does not litter the download folder.
/// </summary>
internal static class PaperTodoDataPaths
{
    internal const string OverrideVariable = "PAPERTODO_DATA_DIR";

    private static readonly Lazy<string> ResolvedDirectory = new(ResolveDataDirectory);

    internal static string DataDirectory => ResolvedDirectory.Value;

    private static string ResolveDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            var overridePath = Path.GetFullPath(overridden);
            Directory.CreateDirectory(overridePath);
            return overridePath;
        }

#if PAPERTODO_USER_DATA_DIRECTORY
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
        {
            profile = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        var target = Path.GetFullPath(Path.Combine(profile, ".PaperTodo"));
        Directory.CreateDirectory(target);
        return TryMigrateLegacyData(target) ? target : Path.GetFullPath(AppContext.BaseDirectory);
#else
        return AppContext.BaseDirectory;
#endif
    }

#if PAPERTODO_USER_DATA_DIRECTORY
    private static bool TryMigrateLegacyData(string targetDirectory)
    {
        var legacyDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        if (string.Equals(targetDirectory, legacyDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var legacyData = Path.Combine(legacyDirectory, "data.json");
        var legacyBackup = Path.Combine(legacyDirectory, "data.backup.json");
        var legacyImages = Path.Combine(legacyDirectory, "note-assets.lmdb");
        if (!File.Exists(legacyData) && !File.Exists(legacyBackup))
        {
            return true;
        }

        var targetData = Path.Combine(targetDirectory, "data.json");
        var targetBackup = Path.Combine(targetDirectory, "data.backup.json");
        if (File.Exists(targetData) || File.Exists(targetBackup))
        {
            return true;
        }

        var copiedTargets = new List<string>();
        var copiedSources = new List<string>();
        try
        {
            CopyIfExists(legacyData, targetData, copiedTargets, copiedSources);
            CopyIfExists(legacyBackup, targetBackup, copiedTargets, copiedSources);
            CopyIfExists(legacyImages, Path.Combine(targetDirectory, "note-assets.lmdb"), copiedTargets, copiedSources);
            if (!File.Exists(targetData) && !File.Exists(targetBackup))
            {
                return false;
            }

            // Best effort, and only for files actually copied. A failed cleanup leaves a second
            // copy behind; it never deletes a legacy file that was not migrated.
            foreach (var source in copiedSources)
            {
                try
                {
                    if (File.Exists(source))
                    {
                        File.Delete(source);
                    }
                }
                catch
                {
                    // Keep the legacy copy if it cannot be removed.
                }
            }

            return true;
        }
        catch
        {
            foreach (var target in copiedTargets)
            {
                try
                {
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                }
                catch
                {
                    // A partially copied target is retried on the next launch.
                }
            }

            return false;
        }
    }

    private static void CopyIfExists(
        string source,
        string target,
        List<string> copiedTargets,
        List<string> copiedSources)
    {
        if (!File.Exists(source) || File.Exists(target))
        {
            return;
        }

        File.Copy(source, target, overwrite: false);
        copiedTargets.Add(target);
        copiedSources.Add(source);
    }
#endif
}
