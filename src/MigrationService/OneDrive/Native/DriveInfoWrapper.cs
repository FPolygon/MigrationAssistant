using System.Runtime.Versioning;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Wrapper for System.IO.DriveInfo that implements IDriveInfo
/// </summary>
[SupportedOSPlatform("windows")]
public class DriveInfoWrapper : IDriveInfo
{
    private readonly DriveInfo _driveInfo;

    public DriveInfoWrapper(DriveInfo driveInfo)
    {
        _driveInfo = driveInfo ?? throw new ArgumentNullException(nameof(driveInfo));
    }

    /// <inheritdoc/>
    public long AvailableFreeSpace => _driveInfo.AvailableFreeSpace;

    /// <inheritdoc/>
    public string Name => _driveInfo.Name;
}
