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
    private readonly Dictionary<string, DirectoryInfo> _directoryInfos = new();
    private readonly Dictionary<string, FileInfo> _fileInfos = new();
    private readonly Dictionary<string, DriveInfo> _driveInfos = new();
    private readonly Dictionary<string, long> _availableFreeSpace = new();

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

    /// <inheritdoc/>
    public Task<bool> DirectoryExistsAsync(string path)
    {
        return Task.FromResult(_existingDirectories.Contains(path));
    }

    /// <inheritdoc/>
    public Task<DirectoryInfo?> GetDirectoryInfoAsync(string path)
    {
        if (_directoryInfos.TryGetValue(path, out var dirInfo))
        {
            return Task.FromResult<DirectoryInfo?>(dirInfo);
        }

        if (_existingDirectories.Contains(path))
        {
            return Task.FromResult<DirectoryInfo?>(new MockDirectoryInfo(path, DateTime.UtcNow, true) as DirectoryInfo);
        }

        return Task.FromResult<DirectoryInfo?>(null);
    }

    /// <inheritdoc/>
    public Task<DriveInfo?> GetDriveInfoAsync(string path)
    {
        var rootPath = GetPathRoot(path);
        if (rootPath != null && _driveInfos.TryGetValue(rootPath, out var driveInfo))
        {
            return Task.FromResult<DriveInfo?>(driveInfo);
        }

        // Default drive info for test scenarios
        if (rootPath != null)
        {
            return Task.FromResult<DriveInfo?>(new MockDriveInfo(rootPath, 1073741824)); // 1GB default
        }

        return Task.FromResult<DriveInfo?>(null);
    }

    /// <inheritdoc/>
    public Task<FileInfo[]> GetFilesAsync(string path, string searchPattern, SearchOption searchOption)
    {
        // For testing, return empty array unless specifically configured
        return Task.FromResult(Array.Empty<FileInfo>());
    }

    /// <inheritdoc/>
    public Task<FileInfo?> GetFileInfoAsync(string path)
    {
        if (_fileInfos.TryGetValue(path, out var fileInfo))
        {
            return Task.FromResult<FileInfo?>(fileInfo);
        }

        if (_existingFiles.Contains(path))
        {
            return Task.FromResult<FileInfo?>(new MockFileInfo(path, 1024, true));
        }

        return Task.FromResult<FileInfo?>(null);
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
public class MockDirectoryInfo
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
public class MockFileInfo
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
public class MockDriveInfo
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