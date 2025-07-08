using MigrationTool.Service.ProfileManagement;

namespace MigrationTool.Service.Models;

/// <summary>
/// Represents a user classification record in the database
/// </summary>
public class UserClassificationRecord
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ProfileClassification Classification { get; set; }
    public DateTime ClassificationDate { get; set; }
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? RuleSetName { get; set; }
    public string? RuleSetVersion { get; set; }
    public bool IsOverridden { get; set; }
    public int? ActivityScore { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Represents a classification history entry
/// </summary>
public class ClassificationHistoryEntry
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ProfileClassification? OldClassification { get; set; }
    public ProfileClassification NewClassification { get; set; }
    public DateTime ChangeDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ActivitySnapshot { get; set; } // JSON blob
    public DateTime CreatedAt { get; set; }
}