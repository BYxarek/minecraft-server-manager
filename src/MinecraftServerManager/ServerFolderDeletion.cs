namespace MinecraftServerManager;

public static class ServerFolderDeletion
{
    public static string Validate(ServerProfile profile, IEnumerable<ServerProfile> profiles)
    {
        if (string.IsNullOrWhiteSpace(profile.Directory)) throw new InvalidOperationException(T("UnsafeServerFolder"));
        var path = Normalize(profile.Directory);
        if (path.Equals(Normalize(Path.GetPathRoot(path)!), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(T("UnsafeServerFolder"));

        var protectedPaths = new[]
        {
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        if (protectedPaths.Where(value => !string.IsNullOrWhiteSpace(value)).Any(value => Contains(path, Normalize(value))))
            throw new InvalidOperationException(T("UnsafeServerFolder"));

        foreach (var other in profiles.Where(other => other.Id != profile.Id && !string.IsNullOrWhiteSpace(other.Directory)))
        {
            var otherPath = Normalize(other.Directory);
            if (Contains(path, otherPath) || Contains(otherPath, path))
                throw new InvalidOperationException(T("ServerFolderShared"));
        }
        if (File.Exists(path)) throw new InvalidOperationException(T("UnsafeServerFolder"));
        if (Directory.Exists(path) && new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException(T("UnsafeServerFolder"));
        return path;
    }

    public static void Delete(ServerProfile profile, IEnumerable<ServerProfile> profiles)
    {
        var path = Validate(profile, profiles);
        if (Directory.Exists(path)) DeleteTree(new DirectoryInfo(path));
    }

    private static void DeleteTree(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child)
            {
                if (child.Attributes.HasFlag(FileAttributes.ReparsePoint)) child.Delete();
                else DeleteTree(child);
            }
            else entry.Delete();
        }
        directory.Delete();
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        return full.Length == Path.GetPathRoot(full)!.Length ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
    private static bool Contains(string parent, string child) => child.Equals(parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
