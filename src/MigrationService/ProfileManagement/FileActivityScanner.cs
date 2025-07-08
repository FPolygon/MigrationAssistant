using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Scans file system for user activity patterns efficiently
/// </summary>
[SupportedOSPlatform("windows")]
public class FileActivityScanner
{
    private readonly ILogger<FileActivityScanner> _logger;
    private readonly TimeSpan _recentThreshold;
    private readonly int _maxFilesPerFolder;
    private readonly int _maxDepth;
    private readonly ConcurrentDictionary<string, FolderScanResult> _scanCache;
    
    // Key user folders to prioritize
    private static readonly Dictionary<string, int> PriorityFolders = new()
    {
        { "Desktop", 100 },
        { "Documents", 90 },
        { "Downloads", 85 },
        { "Pictures", 70 },
        { "Videos", 70 },
        { "Music", 60 },
        { @"AppData\Roaming", 80 },
        { @"AppData\Local", 75 },
        { @"AppData\Roaming\Microsoft\Windows\Recent", 95 }
    };

    // Extensions that indicate user activity
    private static readonly HashSet<string> UserFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".pdf",
        ".txt", ".rtf", ".odt", ".ods", ".odp",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg",
        ".mp4", ".avi", ".mkv", ".mov", ".wmv",
        ".mp3", ".wav", ".flac", ".aac",
        ".zip", ".rar", ".7z", ".tar",
        ".psd", ".ai", ".sketch", ".fig",
        ".cs", ".js", ".py", ".java", ".cpp", ".h",
        ".html", ".css", ".json", ".xml", ".yaml"
    };

    // Folders to skip for performance
    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        @"AppData\Local\Microsoft\Windows\INetCache",
        @"AppData\Local\Microsoft\Windows\WebCache",
        @"AppData\Local\Temp",
        @"AppData\Local\Packages",
        @"AppData\Local\Microsoft\WindowsApps",
        @"AppData\Local\Microsoft\Windows\Temporary Internet Files",
        @".git", ".svn", ".vs", "node_modules", "bin", "obj"
    };

    public FileActivityScanner(
        ILogger<FileActivityScanner> logger,
        TimeSpan? recentThreshold = null,
        int? maxFilesPerFolder = null,
        int? maxDepth = null)
    {
        _logger = logger;
        _recentThreshold = recentThreshold ?? TimeSpan.FromDays(30);
        _maxFilesPerFolder = maxFilesPerFolder ?? 100;
        _maxDepth = maxDepth ?? 3;
        _scanCache = new ConcurrentDictionary<string, FolderScanResult>();
    }

    /// <summary>
    /// Scans a user profile for file activity
    /// </summary>
    public async Task<FileActivityReport> ScanProfileActivityAsync(
        string profilePath, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting file activity scan for profile: {Path}", profilePath);

        var report = new FileActivityReport
        {
            ProfilePath = profilePath,
            ScanStartTime = DateTime.UtcNow
        };

        try
        {
            if (!Directory.Exists(profilePath))
            {
                report.Errors.Add($"Profile directory not found: {profilePath}");
                return report;
            }

            // Scan priority folders in parallel
            var scanTasks = new List<Task<FolderScanResult>>();

            foreach (var (folderName, priority) in PriorityFolders.OrderByDescending(kvp => kvp.Value))
            {
                var folderPath = Path.Combine(profilePath, folderName);
                if (Directory.Exists(folderPath))
                {
                    scanTasks.Add(ScanFolderAsync(folderPath, folderName, priority, 0, cancellationToken));
                }
            }

            // Wait for all scans to complete
            var results = await Task.WhenAll(scanTasks);

            // Aggregate results
            foreach (var result in results.Where(r => r != null))
            {
                report.FolderResults[result.FolderName] = result;
                report.TotalFilesScanned += result.FilesScanned;
                report.RecentFileCount += result.RecentFileCount;
                report.VeryRecentFileCount += result.VeryRecentFileCount;

                // Track most recent files
                foreach (var file in result.RecentFiles.Take(10))
                {
                    report.MostRecentFiles.Add(file);
                }
            }

            // Sort most recent files by modification time
            report.MostRecentFiles.Sort((a, b) => b.LastModified.CompareTo(a.LastModified));
            if (report.MostRecentFiles.Count > 20)
            {
                report.MostRecentFiles = report.MostRecentFiles.Take(20).ToList();
            }

            // Calculate activity metrics
            CalculateActivityMetrics(report);

            report.ScanEndTime = DateTime.UtcNow;
            _logger.LogInformation(
                "File activity scan completed for {Path}: {TotalFiles} files, {RecentFiles} recent, Score={Score}",
                profilePath, report.TotalFilesScanned, report.RecentFileCount, report.ActivityScore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan profile activity: {Path}", profilePath);
            report.Errors.Add($"Scan failed: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// Scans a specific folder for activity
    /// </summary>
    private async Task<FolderScanResult> ScanFolderAsync(
        string folderPath, 
        string folderName, 
        int priority, 
        int currentDepth,
        CancellationToken cancellationToken)
    {
        // Check cache first
        var cacheKey = $"{folderPath}_{_recentThreshold.TotalDays}";
        if (_scanCache.TryGetValue(cacheKey, out var cached) && 
            DateTime.UtcNow - cached.ScanTime < TimeSpan.FromMinutes(5))
        {
            return cached;
        }

        var result = new FolderScanResult
        {
            FolderPath = folderPath,
            FolderName = folderName,
            Priority = priority,
            ScanTime = DateTime.UtcNow
        };

        await Task.Run(() =>
        {
            try
            {
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    AttributesToSkip = FileAttributes.System | FileAttributes.Hidden
                };

                var directory = new DirectoryInfo(folderPath);
                
                // Get folder modification time
                result.LastModified = directory.LastWriteTimeUtc;

                // Scan files in current directory
                var files = directory.EnumerateFiles("*", options)
                    .Take(_maxFilesPerFolder)
                    .ToList();

                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    result.FilesScanned++;

                    // Check if it's a user file type
                    if (!IsUserFile(file))
                        continue;

                    var age = DateTime.UtcNow - file.LastWriteTimeUtc;
                    
                    if (age <= _recentThreshold)
                    {
                        result.RecentFileCount++;
                        
                        if (age <= TimeSpan.FromDays(7))
                            result.VeryRecentFileCount++;

                        // Track individual recent files
                        result.RecentFiles.Add(new FileActivityInfo
                        {
                            FilePath = file.FullName,
                            FileName = file.Name,
                            Extension = file.Extension,
                            SizeBytes = file.Length,
                            LastModified = file.LastWriteTimeUtc,
                            LastAccessed = file.LastAccessTimeUtc,
                            IsUserDocument = IsUserDocument(file.Extension)
                        });
                    }

                    // Track file types
                    if (!string.IsNullOrEmpty(file.Extension))
                    {
                        result.FileTypeCount.TryGetValue(file.Extension, out var count);
                        result.FileTypeCount[file.Extension] = count + 1;
                    }
                }

                // Scan subdirectories if within depth limit
                if (currentDepth < _maxDepth)
                {
                    var subDirs = directory.EnumerateDirectories("*", options)
                        .Where(d => !ShouldSkipFolder(d.Name))
                        .Take(10); // Limit subdirectories for performance

                    foreach (var subDir in subDirs)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var subResult = ScanFolderAsync(
                            subDir.FullName, 
                            Path.Combine(folderName, subDir.Name),
                            priority - 10,
                            currentDepth + 1,
                            cancellationToken).Result;

                        // Aggregate sub-results
                        result.FilesScanned += subResult.FilesScanned;
                        result.RecentFileCount += subResult.RecentFileCount;
                        result.VeryRecentFileCount += subResult.VeryRecentFileCount;
                        
                        // Merge recent files
                        foreach (var file in subResult.RecentFiles.Take(5))
                        {
                            result.RecentFiles.Add(file);
                        }
                    }
                }

                // Sort recent files by date
                result.RecentFiles.Sort((a, b) => b.LastModified.CompareTo(a.LastModified));
                
                // Keep only top files
                if (result.RecentFiles.Count > 50)
                {
                    result.RecentFiles = result.RecentFiles.Take(50).ToList();
                }

                // Calculate folder activity score
                result.ActivityScore = CalculateFolderActivityScore(result);

                // Update cache
                _scanCache[cacheKey] = result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan folder: {Path}", folderPath);
                result.Errors.Add($"Scan error: {ex.Message}");
            }
        }, cancellationToken);

        return result;
    }

    /// <summary>
    /// Determines if a file is a user file based on extension
    /// </summary>
    private bool IsUserFile(FileInfo file)
    {
        // Skip very small files (likely system files)
        if (file.Length < 1024) // 1KB
            return false;

        // Skip system files
        if (file.Name.StartsWith("~") || file.Name.StartsWith("."))
            return false;

        return UserFileExtensions.Contains(file.Extension) || 
               file.Extension.Length == 0; // Include files without extensions
    }

    /// <summary>
    /// Determines if a file is a user document
    /// </summary>
    private bool IsUserDocument(string extension)
    {
        var documentExtensions = new[] { ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".pdf", ".txt", ".rtf" };
        return documentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a folder should be skipped
    /// </summary>
    private bool ShouldSkipFolder(string folderName)
    {
        return SkipFolders.Any(skip => folderName.Contains(skip, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Calculates activity score for a folder
    /// </summary>
    private int CalculateFolderActivityScore(FolderScanResult result)
    {
        var score = 0;

        // Base score on priority
        score += result.Priority / 2;

        // Recent files (0-30 points)
        if (result.VeryRecentFileCount > 10) score += 30;
        else if (result.VeryRecentFileCount > 5) score += 20;
        else if (result.VeryRecentFileCount > 0) score += 10;

        // File diversity (0-20 points)
        var uniqueTypes = result.FileTypeCount.Count;
        if (uniqueTypes > 10) score += 20;
        else if (uniqueTypes > 5) score += 15;
        else if (uniqueTypes > 2) score += 10;

        return Math.Min(score, 100);
    }

    /// <summary>
    /// Calculates overall activity metrics
    /// </summary>
    private void CalculateActivityMetrics(FileActivityReport report)
    {
        // Calculate activity score based on various factors
        var score = 0;

        // Very recent files (0-40 points)
        if (report.VeryRecentFileCount > 50) score += 40;
        else if (report.VeryRecentFileCount > 20) score += 30;
        else if (report.VeryRecentFileCount > 10) score += 20;
        else if (report.VeryRecentFileCount > 0) score += 10;

        // Recent files (0-30 points)
        if (report.RecentFileCount > 100) score += 30;
        else if (report.RecentFileCount > 50) score += 20;
        else if (report.RecentFileCount > 20) score += 15;
        else if (report.RecentFileCount > 0) score += 10;

        // Active folders (0-20 points)
        var activeFolders = report.FolderResults.Values.Count(f => f.RecentFileCount > 0);
        if (activeFolders > 5) score += 20;
        else if (activeFolders > 3) score += 15;
        else if (activeFolders > 1) score += 10;

        // Document activity (0-10 points)
        var recentDocs = report.MostRecentFiles.Count(f => f.IsUserDocument);
        if (recentDocs > 5) score += 10;
        else if (recentDocs > 0) score += 5;

        report.ActivityScore = Math.Min(score, 100);

        // Determine activity level
        if (report.ActivityScore >= 70)
            report.ActivityLevel = FileActivityLevel.VeryActive;
        else if (report.ActivityScore >= 40)
            report.ActivityLevel = FileActivityLevel.Active;
        else if (report.ActivityScore >= 20)
            report.ActivityLevel = FileActivityLevel.Moderate;
        else if (report.ActivityScore > 0)
            report.ActivityLevel = FileActivityLevel.Low;
        else
            report.ActivityLevel = FileActivityLevel.Inactive;

        // Find most active time
        if (report.MostRecentFiles.Any())
        {
            report.MostRecentActivity = report.MostRecentFiles.Max(f => f.LastModified);
        }
    }

    /// <summary>
    /// Clears the scan cache
    /// </summary>
    public void ClearCache()
    {
        _scanCache.Clear();
    }
}

/// <summary>
/// Report of file activity scan results
/// </summary>
public class FileActivityReport
{
    public string ProfilePath { get; set; } = string.Empty;
    public DateTime ScanStartTime { get; set; }
    public DateTime ScanEndTime { get; set; }
    
    // Summary statistics
    public int TotalFilesScanned { get; set; }
    public int RecentFileCount { get; set; }
    public int VeryRecentFileCount { get; set; }
    public DateTime MostRecentActivity { get; set; }
    
    // Activity analysis
    public int ActivityScore { get; set; }
    public FileActivityLevel ActivityLevel { get; set; }
    
    // Detailed results
    public Dictionary<string, FolderScanResult> FolderResults { get; set; } = new();
    public List<FileActivityInfo> MostRecentFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Results from scanning a specific folder
/// </summary>
public class FolderScanResult
{
    public string FolderPath { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public int Priority { get; set; }
    public DateTime ScanTime { get; set; }
    public DateTime LastModified { get; set; }
    
    // Statistics
    public int FilesScanned { get; set; }
    public int RecentFileCount { get; set; }
    public int VeryRecentFileCount { get; set; }
    public int ActivityScore { get; set; }
    
    // File type analysis
    public Dictionary<string, int> FileTypeCount { get; set; } = new();
    
    // Recent files
    public List<FileActivityInfo> RecentFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Information about a file's activity
/// </summary>
public class FileActivityInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public DateTime LastAccessed { get; set; }
    public bool IsUserDocument { get; set; }
}

/// <summary>
/// Levels of file activity
/// </summary>
public enum FileActivityLevel
{
    Inactive,
    Low,
    Moderate,
    Active,
    VeryActive
}