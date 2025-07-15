using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Windows-specific implementation of file system operations
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsFileSystemService : IFileSystemService
{
    private readonly ILogger<WindowsFileSystemService> _logger;

    public WindowsFileSystemService(ILogger<WindowsFileSystemService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> DirectoryExistsAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                return Directory.Exists(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check if directory exists: {Path}", path);
                return false;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<DirectoryInfo?> GetDirectoryInfoAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return await Task.Run(() =>
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);
                return dirInfo.Exists ? dirInfo : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get directory info for: {Path}", path);
                return null;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<DriveInfo?> GetDriveInfoAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return await Task.Run(() =>
        {
            try
            {
                var rootPath = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(rootPath))
                {
                    return null;
                }

                return new DriveInfo(rootPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get drive info for: {Path}", path);
                return null;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<FileInfo[]> GetFilesAsync(string path, string searchPattern, SearchOption searchOption)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Array.Empty<FileInfo>();
        }

        return await Task.Run(() =>
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists)
                {
                    return Array.Empty<FileInfo>();
                }

                return dirInfo.GetFiles(searchPattern, searchOption);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get files in directory: {Path}", path);
                return Array.Empty<FileInfo>();
            }
        });
    }

    /// <inheritdoc/>
    public async Task<FileInfo?> GetFileInfoAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return await Task.Run(() =>
        {
            try
            {
                var fileInfo = new FileInfo(path);
                return fileInfo.Exists ? fileInfo : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get file info for: {Path}", path);
                return null;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<bool> FileExistsAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                return File.Exists(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check if file exists: {Path}", path);
                return false;
            }
        });
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get path root for: {Path}", path);
            return null;
        }
    }
}