using System.Runtime.Versioning;
using MigrationTool.Service.OneDrive.Native;

namespace MigrationService.Tests.OneDrive.TestUtilities;

/// <summary>
/// Test implementation of IFileSystemService for unit testing
/// </summary>
[SupportedOSPlatform("windows")]
public class TestFileSystemService : IFileSystemService
{
    private readonly HashSet<string> _existingDirectories = new();
    private readonly HashSet<string> _existingFiles = new();
    private readonly Dictionary<string, IDirectoryInfo> _directoryInfos = new();
    private readonly Dictionary<string, IFileInfo> _fileInfos = new();
    private readonly Dictionary<string, IDriveInfo> _driveInfos = new();
    private readonly Dictionary<string, long> _availableFreeSpace = new();
    private readonly Dictionary<string, List<IFileInfo>> _directoryFiles = new();

    /// <summary>
    /// Configures a directory to exist in the test file system
    /// </summary>
    public void SetDirectoryExists(string path, bool exists = true)
    {
        if (exists)
        {
            _existingDirectories.Add(path);
        }
        else
        {
            _existingDirectories.Remove(path);
        }
    }

    /// <summary>
    /// Configures a file to exist in the test file system
    /// </summary>
    public void SetFileExists(string path, bool exists = true)
    {
        if (exists)
        {
            _existingFiles.Add(path);
        }
        else
        {
            _existingFiles.Remove(path);
        }
    }

    /// <summary>
    /// Configures directory information for a path
    /// </summary>
    public void SetDirectoryInfo(string path, DateTime lastWriteTime)
    {
        var mockDirInfo = new MockDirectoryInfo(path, lastWriteTime, true);
        _directoryInfos[path] = mockDirInfo;
    }

    /// <summary>
    /// Configures available free space for a drive
    /// </summary>
    public void SetAvailableFreeSpace(string rootPath, long freeSpaceBytes)
    {
        _availableFreeSpace[rootPath] = freeSpaceBytes;
        var mockDriveInfo = new MockDriveInfo(rootPath, freeSpaceBytes);
        _driveInfos[rootPath] = mockDriveInfo;
    }

    /// <summary>
    /// Configures files to exist in a specific directory
    /// </summary>
    public void SetFilesForDirectory(string directoryPath, params IFileInfo[] files)
    {
        _directoryFiles[directoryPath] = files.ToList();

        // Also add files to the existing files set
        foreach (var file in files)
        {
            _existingFiles.Add(file.FullName);
            _fileInfos[file.FullName] = file;
        }
    }

    /// <summary>
    /// Adds a file to a directory
    /// </summary>
    public void AddFileToDirectory(string directoryPath, string fileName, long fileSize = 1024)
    {
        var filePath = Path.Combine(directoryPath, fileName);
        var fileInfo = new MockFileInfo(filePath, fileSize, true);

        if (!_directoryFiles.ContainsKey(directoryPath))
        {
            _directoryFiles[directoryPath] = new List<IFileInfo>();
        }

        _directoryFiles[directoryPath].Add(fileInfo);
        _existingFiles.Add(filePath);
        _fileInfos[filePath] = fileInfo;
    }

    /// <summary>
    /// Clears all configured files and directories
    /// </summary>
    public void ClearAll()
    {
        _existingDirectories.Clear();
        _existingFiles.Clear();
        _directoryInfos.Clear();
        _fileInfos.Clear();
        _driveInfos.Clear();
        _availableFreeSpace.Clear();
        _directoryFiles.Clear();
    }

    /// <inheritdoc/>
    public Task<bool> DirectoryExistsAsync(string path)
    {
        return Task.FromResult(_existingDirectories.Contains(path));
    }

    /// <inheritdoc/>
    public Task<IDirectoryInfo?> GetDirectoryInfoAsync(string path)
    {
        if (_directoryInfos.TryGetValue(path, out var dirInfo))
        {
            return Task.FromResult<IDirectoryInfo?>(dirInfo);
        }

        if (_existingDirectories.Contains(path))
        {
            return Task.FromResult<IDirectoryInfo?>(new MockDirectoryInfo(path, DateTime.UtcNow, true));
        }

        return Task.FromResult<IDirectoryInfo?>(null);
    }

    /// <inheritdoc/>
    public Task<IDriveInfo?> GetDriveInfoAsync(string path)
    {
        var rootPath = GetPathRoot(path);
        if (rootPath != null && _driveInfos.TryGetValue(rootPath, out var driveInfo))
        {
            return Task.FromResult<IDriveInfo?>(driveInfo);
        }

        // Default drive info for test scenarios
        if (rootPath != null)
        {
            return Task.FromResult<IDriveInfo?>(new MockDriveInfo(rootPath, 1073741824)); // 1GB default
        }

        return Task.FromResult<IDriveInfo?>(null);
    }

    /// <inheritdoc/>
    public Task<IFileInfo[]> GetFilesAsync(string path, string searchPattern, SearchOption searchOption)
    {
        var results = new List<IFileInfo>();

        try
        {
            // Get files from the current directory
            if (_directoryFiles.TryGetValue(path, out var files))
            {
                var matchingFiles = files.Where(f => MatchesPattern(Path.GetFileName(f.FullName), searchPattern));
                results.AddRange(matchingFiles);
            }

            // If AllDirectories is specified, search subdirectories recursively
            if (searchOption == SearchOption.AllDirectories)
            {
                var subdirectories = _directoryFiles.Keys.Where(dir =>
                    IsSubdirectory(dir, path) && !string.Equals(dir, path, StringComparison.OrdinalIgnoreCase));

                foreach (var subdirectory in subdirectories)
                {
                    if (_directoryFiles.TryGetValue(subdirectory, out var subFiles))
                    {
                        var matchingSubFiles = subFiles.Where(f => MatchesPattern(Path.GetFileName(f.FullName), searchPattern));
                        results.AddRange(matchingSubFiles);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't fail the test
            Console.WriteLine($"Error in GetFilesAsync: {ex.Message}");
        }

        return Task.FromResult(results.ToArray());
    }

    /// <summary>
    /// Checks if a filename matches a search pattern
    /// </summary>
    private bool MatchesPattern(string fileName, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
        {
            return true;
        }

        if (pattern == "*.*")
        {
            return true;
        }

        // Handle simple wildcard patterns
        if (pattern.StartsWith("*") && pattern.Length > 1)
        {
            var extension = pattern.Substring(1);
            return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.EndsWith("*") && pattern.Length > 1)
        {
            var prefix = pattern.Substring(0, pattern.Length - 1);
            return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.Contains("*"))
        {
            // Convert wildcard pattern to regex
            var regexPattern = "^" + pattern.Replace("*", ".*").Replace("?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(fileName, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a directory is a subdirectory of another directory
    /// </summary>
    private bool IsSubdirectory(string subdirectory, string parentDirectory)
    {
        try
        {
            var parentPath = Path.GetFullPath(parentDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var subPath = Path.GetFullPath(subdirectory).TrimEnd(Path.DirectorySeparatorChar);

            return subPath.StartsWith(parentPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<IFileInfo?> GetFileInfoAsync(string path)
    {
        if (_fileInfos.TryGetValue(path, out var fileInfo))
        {
            return Task.FromResult<IFileInfo?>(fileInfo);
        }

        if (_existingFiles.Contains(path))
        {
            return Task.FromResult<IFileInfo?>(new MockFileInfo(path, 1024, true));
        }

        return Task.FromResult<IFileInfo?>(null);
    }

    /// <inheritdoc/>
    public Task<bool> FileExistsAsync(string path)
    {
        return Task.FromResult(_existingFiles.Contains(path));
    }

    /// <inheritdoc/>
    public string? GetPathRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetPathRoot(path);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Mock implementation of DirectoryInfo for testing
/// </summary>
public class MockDirectoryInfo : IDirectoryInfo
{
    private readonly DateTime _lastWriteTimeUtc;
    private readonly bool _exists;
    private readonly string _path;

    public MockDirectoryInfo(string path, DateTime lastWriteTimeUtc, bool exists = true)
    {
        _path = path;
        _lastWriteTimeUtc = lastWriteTimeUtc;
        _exists = exists;
    }

    public DateTime LastWriteTimeUtc => _lastWriteTimeUtc;
    public bool Exists => _exists;
    public string FullName => _path;
    public FileAttributes Attributes => FileAttributes.Directory;
}

/// <summary>
/// Mock implementation of FileInfo for testing
/// </summary>
public class MockFileInfo : IFileInfo
{
    private readonly long _length;
    private readonly bool _exists;
    private readonly string _path;

    public MockFileInfo(string fileName, long length, bool exists = true)
    {
        _path = fileName;
        _length = length;
        _exists = exists;
    }

    public long Length => _length;
    public bool Exists => _exists;
    public string FullName => _path;
    public FileAttributes Attributes => FileAttributes.Normal;
}

/// <summary>
/// Mock implementation of DriveInfo for testing
/// </summary>
public class MockDriveInfo : IDriveInfo
{
    private readonly long _availableFreeSpace;
    private readonly string _name;

    public MockDriveInfo(string driveName, long availableFreeSpace)
    {
        _name = driveName;
        _availableFreeSpace = availableFreeSpace;
    }

    public long AvailableFreeSpace => _availableFreeSpace;
    public string Name => _name;
}
