using System.Runtime.Versioning;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Wrapper for System.IO.FileInfo that implements IFileInfo
/// </summary>
[SupportedOSPlatform("windows")]
public class FileInfoWrapper : IFileInfo
{
    private readonly FileInfo _fileInfo;

    public FileInfoWrapper(FileInfo fileInfo)
    {
        _fileInfo = fileInfo ?? throw new ArgumentNullException(nameof(fileInfo));
    }

    /// <inheritdoc/>
    public long Length => _fileInfo.Length;

    /// <inheritdoc/>
    public bool Exists => _fileInfo.Exists;

    /// <inheritdoc/>
    public string FullName => _fileInfo.FullName;

    /// <inheritdoc/>
    public FileAttributes Attributes => _fileInfo.Attributes;
}
