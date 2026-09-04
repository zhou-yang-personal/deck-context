using System.Security.Cryptography;
using System.Text.Json;

namespace DeckContext.Pipeline;

internal static class ContextPackageDirectoryPublisher
{
    public static string CreateStagingDirectory(string targetDirectory)
    {
        ValidateTarget(targetDirectory);
        var parentDirectory = Path.GetDirectoryName(targetDirectory) ??
                              throw new InvalidOperationException("The output directory must have a parent directory.");
        var targetName = Path.GetFileName(targetDirectory);

        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new InvalidOperationException("A drive or filesystem root cannot be used as the output directory.");
        }

        Directory.CreateDirectory(parentDirectory);
        var stagingDirectory = Path.Combine(
            parentDirectory,
            $".{targetName}.deckcontext-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        return stagingDirectory;
    }

    public static void Publish(string stagingDirectory, string targetDirectory)
    {
        ValidateTarget(targetDirectory);
        var parentDirectory = Path.GetDirectoryName(targetDirectory)!;
        var backupDirectory = Path.Combine(
            parentDirectory,
            $".{Path.GetFileName(targetDirectory)}.deckcontext-backup-{Guid.NewGuid():N}");
        var movedExistingTarget = false;

        try
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Move(targetDirectory, backupDirectory);
                movedExistingTarget = true;
            }

            Directory.Move(stagingDirectory, targetDirectory);
        }
        catch (Exception publishException)
        {
            if (movedExistingTarget &&
                !Directory.Exists(targetDirectory) &&
                Directory.Exists(backupDirectory))
            {
                try
                {
                    Directory.Move(backupDirectory, targetDirectory);
                    movedExistingTarget = false;
                }
                catch (Exception restoreException)
                {
                    throw new IOException(
                        $"Publishing the context package failed and the previous package could not be restored. " +
                        $"The recoverable backup remains at '{backupDirectory}'.",
                        new AggregateException(publishException, restoreException));
                }
            }

            throw;
        }

        if (movedExistingTarget && Directory.Exists(backupDirectory))
        {
            Directory.Delete(backupDirectory, recursive: true);
        }
    }

    public static void DeleteStagingDirectory(string? stagingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(stagingDirectory) && Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static void ValidateTarget(string targetDirectory)
    {
        if (File.Exists(targetDirectory))
        {
            throw new IOException("The output path points to a file instead of a directory.");
        }

        if (!Directory.Exists(targetDirectory))
        {
            return;
        }

        if ((File.GetAttributes(targetDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("A symbolic link or reparse point cannot be replaced as an output directory.");
        }

        if (!Directory.EnumerateFileSystemEntries(targetDirectory).Any())
        {
            return;
        }

        var existingFiles = Directory.EnumerateFiles(targetDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToArray();
        var existingDirectories = Directory.EnumerateDirectories(targetDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToArray();

        var manifestPath = Path.Combine(targetDirectory, "manifest.json");

        if (!File.Exists(manifestPath) ||
            !IsCompleteOwnedPackage(targetDirectory, manifestPath, existingFiles, existingDirectories))
        {
            throw new InvalidOperationException(
                "The output directory is not an intact DeckContext package. Choose a new or empty directory to avoid overwriting unrelated files.");
        }
    }

    private static bool IsCompleteOwnedPackage(
        string targetDirectory,
        string manifestPath,
        IReadOnlyCollection<string> existingFiles,
        IReadOnlyCollection<string> existingDirectories)
    {
        try
        {
            if ((File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

            if (!manifest.RootElement.TryGetProperty("schemaVersion", out _) ||
                !manifest.RootElement.TryGetProperty("sourceFileName", out _) ||
                !manifest.RootElement.TryGetProperty("assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(manifestPath),
            };
            var allowedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fullTargetDirectory = Path.GetFullPath(targetDirectory);
            var targetPrefix = Path.GetFullPath(targetDirectory) + Path.DirectorySeparatorChar;

            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("relativePath", out var relativePathProperty) ||
                    relativePathProperty.GetString() is not { } relativePath ||
                    !asset.TryGetProperty("sha256", out var sha256Property) ||
                    sha256Property.GetString() is not { } expectedSha256 ||
                    !asset.TryGetProperty("sizeBytes", out var sizeBytesProperty) ||
                    !sizeBytesProperty.TryGetInt64(out var expectedSizeBytes))
                {
                    return false;
                }

                var assetPath = Path.GetFullPath(Path.Combine(
                    targetDirectory,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));

                if (!assetPath.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(assetPath) ||
                    (File.GetAttributes(assetPath) & FileAttributes.ReparsePoint) != 0 ||
                    new FileInfo(assetPath).Length != expectedSizeBytes ||
                    !HasExpectedHash(assetPath, expectedSha256))
                {
                    return false;
                }

                allowedFiles.Add(assetPath);
                AddParentDirectories(allowedDirectories, Path.GetDirectoryName(assetPath), fullTargetDirectory);
            }

            return existingFiles.All(allowedFiles.Contains) &&
                   allowedFiles.Count == existingFiles.Count &&
                   existingDirectories.All(allowedDirectories.Contains) &&
                   allowedDirectories.Count == existingDirectories.Count;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasExpectedHash(string path, string expectedSha256)
    {
        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal);
    }

    private static void AddParentDirectories(
        ISet<string> directories,
        string? directory,
        string targetDirectory)
    {
        while (directory is not null &&
               !string.Equals(directory, targetDirectory, StringComparison.OrdinalIgnoreCase))
        {
            directories.Add(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }
}
