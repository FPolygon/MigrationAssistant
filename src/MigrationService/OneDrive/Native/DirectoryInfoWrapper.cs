using System.Runtime.Versioning;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Wrapper for System.IO.DirectoryInfo that implements IDirectoryInfo
/// </summary>
[SupportedOSPlatform("windows")]
public class DirectoryInfoWrapper : IDirectoryInfo
{
    private readonly DirectoryInfo _directoryInfo;

    public DirectoryInfoWrapper(DirectoryInfo directoryInfo)
    {
        _directoryInfo = directoryInfo ?? throw new ArgumentNullException(nameof(directoryInfo));
    }

    /// <inheritdoc/>
    public DateTime LastWriteTimeUtc => _directoryInfo.LastWriteTimeUtc;

    /// <inheritdoc/>
    public bool Exists => _directoryInfo.Exists;

    /// <inheritdoc/>
    public string FullName => _directoryInfo.FullName;

    /// <inheritdoc/>
    public FileAttributes Attributes => _directoryInfo.Attributes;
}
