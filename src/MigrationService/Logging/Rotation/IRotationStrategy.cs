using System.Threading;
using System.Threading.Tasks;

namespace MigrationTool.Service.Logging.Rotation;

/// <summary>
/// Interface for file rotation strategies.
/// </summary>
public interface IRotationStrategy
{
    /// <summary>
    /// Determines whether the current file should be rotated.
    /// </summary>
    /// <param name="currentFilePath">The path of the current log file.</param>
    /// <param name="currentFileSize">The current size of the log file in bytes.</param>
    /// <returns>True if the file should be rotated; otherwise, false.</returns>
    bool ShouldRotate(string currentFilePath, long currentFileSize);

    /// <summary>
    /// Generates the next log file name.
    /// </summary>
    /// <param name="baseFileName">The base file name without extension.</param>
    /// <param name="extension">The file extension.</param>
    /// <returns>The next log file name.</returns>
    string GenerateNextFileName(string baseFileName, string extension);

    /// <summary>
    /// Performs post-rotation cleanup if necessary.
    /// </summary>
    /// <param name="rotatedFilePath">The path of the rotated file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PostRotationCleanupAsync(string rotatedFilePath, CancellationToken cancellationToken = default);
}
