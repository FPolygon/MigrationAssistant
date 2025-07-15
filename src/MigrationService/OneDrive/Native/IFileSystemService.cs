using System.Runtime.Versioning;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Abstraction for file system operations to improve testability
/// </summary>
[SupportedOSPlatform("windows")]
public interface IFileSystemService
{
    /// <summary>
    /// Checks if a directory exists at the specified path
    /// </summary>
    /// <param name="path">The directory path to check</param>
    /// <returns>True if the directory exists, false otherwise</returns>
    Task<bool> DirectoryExistsAsync(string path);

    /// <summary>
    /// Gets directory information for the specified path
    /// </summary>
    /// <param name="path">The directory path</param>
    /// <returns>Directory information or null if directory doesn't exist</returns>
    Task<IDirectoryInfo?> GetDirectoryInfoAsync(string path);

    /// <summary>
    /// Gets drive information for the specified path
    /// </summary>
    /// <param name="path">The path to get drive information for</param>
    /// <returns>Drive information or null if path is invalid</returns>
    Task<IDriveInfo?> GetDriveInfoAsync(string path);

    /// <summary>
    /// Gets files in a directory matching the specified pattern
    /// </summary>
    /// <param name="path">The directory path</param>
    /// <param name="searchPattern">The search pattern (e.g., "*.txt")</param>
    /// <param name="searchOption">The search option (e.g., TopDirectoryOnly, AllDirectories)</param>
    /// <returns>Array of file information</returns>
    Task<IFileInfo[]> GetFilesAsync(string path, string searchPattern, SearchOption searchOption);

    /// <summary>
    /// Gets file information for the specified path
    /// </summary>
    /// <param name="path">The file path</param>
    /// <returns>File information or null if file doesn't exist</returns>
    Task<IFileInfo?> GetFileInfoAsync(string path);

    /// <summary>
    /// Checks if a file exists at the specified path
    /// </summary>
    /// <param name="path">The file path to check</param>
    /// <returns>True if the file exists, false otherwise</returns>
    Task<bool> FileExistsAsync(string path);

    /// <summary>
    /// Gets the root path of the drive containing the specified path
    /// </summary>
    /// <param name="path">The path to get the root for</param>
    /// <returns>The root path or null if invalid</returns>
    string? GetPathRoot(string path);
}

/// <summary>
/// Abstraction for directory information
/// </summary>
public interface IDirectoryInfo
{
    DateTime LastWriteTimeUtc { get; }
    bool Exists { get; }
    string FullName { get; }
    FileAttributes Attributes { get; }
}

/// <summary>
/// Abstraction for file information
/// </summary>
public interface IFileInfo
{
    long Length { get; }
    bool Exists { get; }
    string FullName { get; }
    FileAttributes Attributes { get; }
}

/// <summary>
/// Abstraction for drive information
/// </summary>
public interface IDriveInfo
{
    long AvailableFreeSpace { get; }
    string Name { get; }
}
