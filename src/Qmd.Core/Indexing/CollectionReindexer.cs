using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Qmd.Core.Content;
using Qmd.Core.Models;
using Qmd.Core.Paths;
using Qmd.Core.Store;

namespace Qmd.Core.Indexing;

/// <summary>
/// Re-indexes a collection by scanning the filesystem and updating the database.
/// </summary>
internal static class CollectionReindexer
{
    private static readonly string[] ExcludeDirs = ["node_modules", ".git", ".cache", "vendor", "dist", "build"];

    public static async Task<ReindexResult> ReindexCollectionAsync(
        QmdStore store,
        string collectionPath,
        string globPattern,
        string collectionName,
        ReindexOptions? options = null)
    {
        var now = DateTime.UtcNow.ToString("o");
        var documentRepo = store.DocumentRepo;
        var maintenanceRepo = store.MaintenanceRepo;

        // Find files using FileSystemGlobbing
        var matcher = new Matcher();
        matcher.AddInclude(globPattern);

        // Add exclusions
        foreach (var dir in ExcludeDirs)
            matcher.AddExclude($"**/{dir}/**");
        foreach (var pattern in options?.IgnorePatterns ?? [])
            matcher.AddExclude(pattern);

        var dirInfo = new DirectoryInfoWrapper(new DirectoryInfo(collectionPath));
        var matchResult = matcher.Execute(dirInfo);

        // Filter hidden files
        var files = matchResult.Files
            .Select(f => f.Path)
            .Where(f => !f.Split('/').Any(part => part.StartsWith('.')))
            .ToList();

        var total = files.Count;
        int indexed = 0, updated = 0, unchanged = 0, processed = 0, removed = 0;
        var seenPaths = new HashSet<string>();

        foreach (var relativeFile in files)
        {
            var filepath = Path.GetFullPath(Path.Combine(collectionPath, relativeFile));
            var handelized = Handelize.Convert(relativeFile);
            seenPaths.Add(handelized);

            string content;
            try
            {
                content = await File.ReadAllTextAsync(filepath);
            }
            catch (IOException)
            {
                processed++;
                options?.OnProgress?.Invoke(new ReindexProgress(relativeFile, processed, total));
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                processed++;
                options?.OnProgress?.Invoke(new ReindexProgress(relativeFile, processed, total));
                continue;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                processed++;
                continue;
            }

            var hash = ContentHasher.HashContent(content);
            var title = TitleExtractor.ExtractTitle(content, relativeFile);
            var existing = documentRepo.FindActiveDocument(collectionName, handelized);

            if (existing != null)
            {
                if (existing.Hash == hash)
                {
                    if (existing.Title != title)
                    {
                        documentRepo.UpdateDocumentTitle(existing.Id, title, now);
                        updated++;
                    }
                    else
                    {
                        unchanged++;
                    }
                }
                else
                {
                    documentRepo.InsertContent(hash, content, now);
                    var modTime = File.GetLastWriteTimeUtc(filepath).ToString("o");
                    documentRepo.UpdateDocument(existing.Id, title, hash, modTime);
                    updated++;
                }
            }
            else
            {
                documentRepo.InsertContent(hash, content, now);
                var fileInfo = new FileInfo(filepath);
                documentRepo.InsertDocument(collectionName, handelized, title, hash,
                    fileInfo.CreationTimeUtc.ToString("o"),
                    fileInfo.LastWriteTimeUtc.ToString("o"));
                indexed++;
            }

            processed++;
            options?.OnProgress?.Invoke(new ReindexProgress(relativeFile, processed, total));
        }

        // Deactivate documents no longer on disk
        var allActive = documentRepo.GetActiveDocumentPaths(collectionName);
        foreach (var path in allActive)
        {
            if (!seenPaths.Contains(path))
            {
                documentRepo.DeactivateDocument(collectionName, path);
                removed++;
            }
        }

        var orphanedCleaned = maintenanceRepo.CleanupOrphanedContent();

        return new ReindexResult(indexed, updated, unchanged, removed, orphanedCleaned);
    }
}
