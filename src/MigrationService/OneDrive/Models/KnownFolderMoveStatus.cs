namespace MigrationTool.Service.OneDrive.Models;

/// <summary>
/// Status of Known Folder Move configuration
/// </summary>
public class KnownFolderMoveStatus
{
    /// <summary>
    /// Whether Known Folder Move is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether the Desktop folder is redirected to OneDrive
    /// </summary>
    public bool DesktopRedirected { get; set; }

    /// <summary>
    /// Path of the Desktop folder if redirected
    /// </summary>
    public string? DesktopPath { get; set; }

    /// <summary>
    /// Whether the Documents folder is redirected to OneDrive
    /// </summary>
    public bool DocumentsRedirected { get; set; }

    /// <summary>
    /// Path of the Documents folder if redirected
    /// </summary>
    public string? DocumentsPath { get; set; }

    /// <summary>
    /// Whether the Pictures folder is redirected to OneDrive
    /// </summary>
    public bool PicturesRedirected { get; set; }

    /// <summary>
    /// Path of the Pictures folder if redirected
    /// </summary>
    public string? PicturesPath { get; set; }

    /// <summary>
    /// Registry path where KFM configuration was found
    /// </summary>
    public string? ConfigurationSource { get; set; }

    /// <summary>
    /// Gets a list of all redirected folders
    /// </summary>
    public List<string> GetRedirectedFolders()
    {
        var folders = new List<string>();

        if (DesktopRedirected)
        {
            folders.Add("Desktop");
        }

        if (DocumentsRedirected)
        {
            folders.Add("Documents");
        }

        if (PicturesRedirected)
        {
            folders.Add("Pictures");
        }

        return folders;
    }

    /// <summary>
    /// Gets the total count of redirected folders
    /// </summary>
    public int RedirectedFolderCount =>
        (DesktopRedirected ? 1 : 0) +
        (DocumentsRedirected ? 1 : 0) +
        (PicturesRedirected ? 1 : 0);
}
