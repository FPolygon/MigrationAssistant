using System.Linq.Expressions;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Models;
using Newtonsoft.Json;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Rule-based engine for user profile classification
/// </summary>
[SupportedOSPlatform("windows")]
public class ClassificationRuleEngine
{
    private readonly ILogger<ClassificationRuleEngine> _logger;
    private readonly List<ClassificationRule> _rules;
    private readonly Dictionary<string, ClassificationRuleSet> _ruleSets;
    private readonly object _rulesLock = new();

    public ClassificationRuleEngine(ILogger<ClassificationRuleEngine> logger)
    {
        _logger = logger;
        _rules = new List<ClassificationRule>();
        _ruleSets = new Dictionary<string, ClassificationRuleSet>(StringComparer.OrdinalIgnoreCase);
        
        // Load default rule sets
        LoadDefaultRuleSets();
    }

    /// <summary>
    /// Evaluates classification rules for a user profile
    /// </summary>
    public async Task<RuleEvaluationResult> EvaluateRulesAsync(
        UserProfile profile,
        ProfileMetrics metrics,
        ActivityScoreResult? activityScore = null,
        string? ruleSetName = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Evaluating classification rules for user: {UserName}", profile.UserName);

        var result = new RuleEvaluationResult
        {
            UserId = profile.UserId,
            UserName = profile.UserName,
            EvaluationTime = DateTime.UtcNow
        };

        try
        {
            // Get the appropriate rule set
            var ruleSet = GetRuleSet(ruleSetName ?? "Default");
            if (ruleSet == null)
            {
                _logger.LogWarning("Rule set not found: {RuleSetName}", ruleSetName);
                result.Errors.Add($"Rule set '{ruleSetName}' not found");
                return result;
            }

            result.RuleSetName = ruleSet.Name;
            result.RuleSetVersion = ruleSet.Version;

            // Create evaluation context
            var context = new RuleEvaluationContext
            {
                Profile = profile,
                Metrics = metrics,
                ActivityScore = activityScore,
                EvaluationTime = DateTime.UtcNow
            };

            // Evaluate rules in priority order
            var sortedRules = ruleSet.Rules.OrderByDescending(r => r.Priority).ToList();
            
            foreach (var rule in sortedRules)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var ruleResult = await EvaluateRuleAsync(rule, context, cancellationToken);
                result.RuleResults.Add(ruleResult);

                // If rule matches and is not continue-on-match, use its classification
                if (ruleResult.IsMatch && !rule.ContinueOnMatch)
                {
                    result.Classification = rule.ResultClassification;
                    result.MatchedRule = rule.Name;
                    result.ClassificationReason = rule.Reason ?? $"Matched rule: {rule.Name}";
                    result.Confidence = ruleResult.Confidence;
                    break;
                }
            }

            // If no rule matched, use default classification
            if (result.Classification == ProfileClassification.Unknown)
            {
                result.Classification = ruleSet.DefaultClassification;
                result.ClassificationReason = "No matching rules, using default";
                result.Confidence = 0.5;
            }

            _logger.LogInformation(
                "Classification result for {UserName}: {Classification} (Rule: {Rule}, Confidence: {Confidence:P})",
                profile.UserName, result.Classification, result.MatchedRule ?? "default", result.Confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate rules for user: {UserName}", profile.UserName);
            result.Errors.Add($"Rule evaluation failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Evaluates a single rule
    /// </summary>
    private async Task<RuleResult> EvaluateRuleAsync(
        ClassificationRule rule, 
        RuleEvaluationContext context, 
        CancellationToken cancellationToken)
    {
        var result = new RuleResult
        {
            RuleName = rule.Name,
            RuleDescription = rule.Description
        };

        try
        {
            // Evaluate each condition
            var conditionResults = new List<bool>();
            var totalWeight = 0.0;
            var weightedScore = 0.0;

            foreach (var condition in rule.Conditions)
            {
                var conditionMatch = await EvaluateConditionAsync(condition, context, cancellationToken);
                conditionResults.Add(conditionMatch);
                
                result.ConditionResults.Add(new ConditionResult
                {
                    ConditionName = condition.Property,
                    IsMatch = conditionMatch,
                    ActualValue = GetPropertyValue(condition.Property, context)?.ToString() ?? "null",
                    ExpectedOperator = condition.Operator.ToString(),
                    ExpectedValue = condition.Value?.ToString() ?? "null"
                });

                // Calculate weighted score if weights are defined
                if (condition.Weight > 0)
                {
                    totalWeight += condition.Weight;
                    if (conditionMatch)
                        weightedScore += condition.Weight;
                }
            }

            // Apply logical operator
            result.IsMatch = rule.LogicalOperator switch
            {
                LogicalOperator.And => conditionResults.All(c => c),
                LogicalOperator.Or => conditionResults.Any(c => c),
                LogicalOperator.Not => !conditionResults.All(c => c),
                LogicalOperator.Weighted => totalWeight > 0 && (weightedScore / totalWeight) >= rule.WeightedThreshold,
                _ => false
            };

            // Calculate confidence
            if (totalWeight > 0)
            {
                result.Confidence = weightedScore / totalWeight;
            }
            else
            {
                result.Confidence = result.IsMatch ? 1.0 : 0.0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate rule: {RuleName}", rule.Name);
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Evaluates a single condition
    /// </summary>
    private async Task<bool> EvaluateConditionAsync(
        RuleCondition condition, 
        RuleEvaluationContext context,
        CancellationToken cancellationToken)
    {
        var actualValue = GetPropertyValue(condition.Property, context);
        
        return condition.Operator switch
        {
            ComparisonOperator.Equals => CompareValues(actualValue, condition.Value) == 0,
            ComparisonOperator.NotEquals => CompareValues(actualValue, condition.Value) != 0,
            ComparisonOperator.GreaterThan => CompareValues(actualValue, condition.Value) > 0,
            ComparisonOperator.GreaterThanOrEqual => CompareValues(actualValue, condition.Value) >= 0,
            ComparisonOperator.LessThan => CompareValues(actualValue, condition.Value) < 0,
            ComparisonOperator.LessThanOrEqual => CompareValues(actualValue, condition.Value) <= 0,
            ComparisonOperator.Contains => actualValue?.ToString()?.Contains(condition.Value?.ToString() ?? "", 
                StringComparison.OrdinalIgnoreCase) ?? false,
            ComparisonOperator.StartsWith => actualValue?.ToString()?.StartsWith(condition.Value?.ToString() ?? "", 
                StringComparison.OrdinalIgnoreCase) ?? false,
            ComparisonOperator.EndsWith => actualValue?.ToString()?.EndsWith(condition.Value?.ToString() ?? "", 
                StringComparison.OrdinalIgnoreCase) ?? false,
            ComparisonOperator.IsNull => actualValue == null,
            ComparisonOperator.IsNotNull => actualValue != null,
            _ => false
        };
    }

    /// <summary>
    /// Gets property value from context
    /// </summary>
    private object? GetPropertyValue(string propertyPath, RuleEvaluationContext context)
    {
        var parts = propertyPath.Split('.');
        if (parts.Length == 0)
            return null;

        return parts[0].ToLowerInvariant() switch
        {
            "profile" => GetNestedPropertyValue(context.Profile, parts.Skip(1).ToArray()),
            "metrics" => GetNestedPropertyValue(context.Metrics, parts.Skip(1).ToArray()),
            "activityscore" => GetNestedPropertyValue(context.ActivityScore, parts.Skip(1).ToArray()),
            "daysincelogin" => (context.EvaluationTime - context.Metrics.LastLoginTime).TotalDays,
            "daysinceactivity" => (context.EvaluationTime - context.Metrics.LastActivityTime).TotalDays,
            "profilesizemb" => context.Metrics.ProfileSizeMB,
            "activeprocesscount" => context.Metrics.ActiveProcessCount,
            _ => null
        };
    }

    /// <summary>
    /// Gets nested property value using reflection
    /// </summary>
    private object? GetNestedPropertyValue(object? obj, string[] propertyPath)
    {
        if (obj == null || propertyPath.Length == 0)
            return obj;

        var property = obj.GetType().GetProperty(propertyPath[0], 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        
        if (property == null)
            return null;

        var value = property.GetValue(obj);
        return propertyPath.Length > 1 ? GetNestedPropertyValue(value, propertyPath.Skip(1).ToArray()) : value;
    }

    /// <summary>
    /// Compares two values
    /// </summary>
    private int CompareValues(object? value1, object? value2)
    {
        if (value1 == null && value2 == null)
            return 0;
        if (value1 == null)
            return -1;
        if (value2 == null)
            return 1;

        // Try to convert to common types for comparison
        if (value1 is IComparable comparable1)
        {
            try
            {
                var converted2 = Convert.ChangeType(value2, value1.GetType());
                return comparable1.CompareTo(converted2);
            }
            catch
            {
                // Fall back to string comparison
            }
        }

        return string.Compare(value1.ToString(), value2.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads default rule sets
    /// </summary>
    private void LoadDefaultRuleSets()
    {
        // Standard rule set
        var standardRuleSet = new ClassificationRuleSet
        {
            Name = "Standard",
            Version = "1.0",
            Description = "Standard classification rules for typical environments",
            DefaultClassification = ProfileClassification.Unknown,
            Rules = new List<ClassificationRule>
            {
                // System account rule
                new ClassificationRule
                {
                    Name = "SystemAccount",
                    Description = "Identifies system and service accounts",
                    Priority = 1000,
                    Conditions = new List<RuleCondition>
                    {
                        new RuleCondition
                        {
                            Property = "Profile.UserId",
                            Operator = ComparisonOperator.StartsWith,
                            Value = "S-1-5-80-"
                        }
                    },
                    LogicalOperator = LogicalOperator.Or,
                    ResultClassification = ProfileClassification.System,
                    Reason = "System or service account"
                },

                // Active user rule
                new ClassificationRule
                {
                    Name = "ActiveUser",
                    Description = "Recently active user with sufficient profile size",
                    Priority = 900,
                    Conditions = new List<RuleCondition>
                    {
                        new RuleCondition
                        {
                            Property = "DaySinceLogin",
                            Operator = ComparisonOperator.LessThan,
                            Value = 30,
                            Weight = 40
                        },
                        new RuleCondition
                        {
                            Property = "ProfileSizeMB",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = 100,
                            Weight = 20
                        },
                        new RuleCondition
                        {
                            Property = "ActivityScore.Score",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = 70,
                            Weight = 40
                        }
                    },
                    LogicalOperator = LogicalOperator.Weighted,
                    WeightedThreshold = 0.7,
                    ResultClassification = ProfileClassification.Active,
                    Reason = "Active user based on recent login and activity"
                },

                // Inactive user rule
                new ClassificationRule
                {
                    Name = "InactiveUser",
                    Description = "User with no recent activity",
                    Priority = 800,
                    Conditions = new List<RuleCondition>
                    {
                        new RuleCondition
                        {
                            Property = "DaySinceLogin",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = 90
                        },
                        new RuleCondition
                        {
                            Property = "DaySinceActivity",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = 90
                        }
                    },
                    LogicalOperator = LogicalOperator.And,
                    ResultClassification = ProfileClassification.Inactive,
                    Reason = "No recent login or activity"
                },

                // Corrupted profile rule
                new ClassificationRule
                {
                    Name = "CorruptedProfile",
                    Description = "Profile with access errors",
                    Priority = 950,
                    Conditions = new List<RuleCondition>
                    {
                        new RuleCondition
                        {
                            Property = "Metrics.IsAccessible",
                            Operator = ComparisonOperator.Equals,
                            Value = false
                        }
                    },
                    LogicalOperator = LogicalOperator.And,
                    ResultClassification = ProfileClassification.Corrupted,
                    Reason = "Profile corrupted or inaccessible"
                }
            }
        };

        // Strict rule set
        var strictRuleSet = new ClassificationRuleSet
        {
            Name = "Strict",
            Version = "1.0",
            Description = "Strict classification rules with higher thresholds",
            DefaultClassification = ProfileClassification.Inactive,
            Rules = new List<ClassificationRule>(standardRuleSet.Rules)
        };

        // Modify thresholds for strict rules
        var strictActiveRule = strictRuleSet.Rules.First(r => r.Name == "ActiveUser");
        strictActiveRule.Conditions.First(c => c.Property == "DaySinceLogin").Value = 14;
        strictActiveRule.Conditions.First(c => c.Property == "ProfileSizeMB").Value = 500;
        strictActiveRule.WeightedThreshold = 0.8;

        // Add rule sets
        lock (_rulesLock)
        {
            _ruleSets["Default"] = standardRuleSet;
            _ruleSets["Standard"] = standardRuleSet;
            _ruleSets["Strict"] = strictRuleSet;
        }
    }

    /// <summary>
    /// Gets a rule set by name
    /// </summary>
    public ClassificationRuleSet? GetRuleSet(string name)
    {
        lock (_rulesLock)
        {
            return _ruleSets.TryGetValue(name, out var ruleSet) ? ruleSet : null;
        }
    }

    /// <summary>
    /// Adds or updates a rule set
    /// </summary>
    public void AddOrUpdateRuleSet(ClassificationRuleSet ruleSet)
    {
        if (string.IsNullOrWhiteSpace(ruleSet.Name))
            throw new ArgumentException("Rule set name is required");

        lock (_rulesLock)
        {
            _ruleSets[ruleSet.Name] = ruleSet;
        }

        _logger.LogInformation("Added/updated rule set: {RuleSetName} (Version: {Version})", 
            ruleSet.Name, ruleSet.Version);
    }

    /// <summary>
    /// Exports rule set to JSON
    /// </summary>
    public string ExportRuleSet(string name)
    {
        var ruleSet = GetRuleSet(name);
        if (ruleSet == null)
            throw new ArgumentException($"Rule set '{name}' not found");

        return JsonConvert.SerializeObject(ruleSet, Formatting.Indented);
    }

    /// <summary>
    /// Imports rule set from JSON
    /// </summary>
    public void ImportRuleSet(string json)
    {
        var ruleSet = JsonConvert.DeserializeObject<ClassificationRuleSet>(json);
        if (ruleSet == null)
            throw new ArgumentException("Invalid rule set JSON");

        AddOrUpdateRuleSet(ruleSet);
    }

    /// <summary>
    /// Validates a rule set
    /// </summary>
    public ValidationResult ValidateRuleSet(ClassificationRuleSet ruleSet)
    {
        var result = new ValidationResult();

        // Check basic properties
        if (string.IsNullOrWhiteSpace(ruleSet.Name))
            result.Errors.Add("Rule set name is required");

        if (string.IsNullOrWhiteSpace(ruleSet.Version))
            result.Errors.Add("Rule set version is required");

        // Check rules
        if (ruleSet.Rules == null || !ruleSet.Rules.Any())
        {
            result.Warnings.Add("Rule set has no rules defined");
        }
        else
        {
            // Check for duplicate rule names
            var duplicateNames = ruleSet.Rules
                .GroupBy(r => r.Name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var name in duplicateNames)
            {
                result.Errors.Add($"Duplicate rule name: {name}");
            }

            // Validate each rule
            foreach (var rule in ruleSet.Rules)
            {
                ValidateRule(rule, result);
            }
        }

        result.IsValid = !result.Errors.Any();
        return result;
    }

    /// <summary>
    /// Validates a single rule
    /// </summary>
    private void ValidateRule(ClassificationRule rule, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(rule.Name))
            result.Errors.Add("Rule name is required");

        if (rule.Conditions == null || !rule.Conditions.Any())
            result.Errors.Add($"Rule '{rule.Name}' has no conditions");

        if (rule.LogicalOperator == LogicalOperator.Weighted && rule.WeightedThreshold <= 0)
            result.Errors.Add($"Rule '{rule.Name}' uses weighted logic but has invalid threshold");

        // Validate conditions
        foreach (var condition in rule.Conditions ?? new List<RuleCondition>())
        {
            if (string.IsNullOrWhiteSpace(condition.Property))
                result.Errors.Add($"Condition in rule '{rule.Name}' has no property specified");

            if (condition.Operator == ComparisonOperator.Unknown)
                result.Errors.Add($"Condition in rule '{rule.Name}' has invalid operator");

            // Check weight for weighted rules
            if (rule.LogicalOperator == LogicalOperator.Weighted && condition.Weight <= 0)
                result.Warnings.Add($"Condition '{condition.Property}' in weighted rule '{rule.Name}' has no weight");
        }
    }
}

/// <summary>
/// Represents a classification rule set
/// </summary>
public class ClassificationRuleSet
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ProfileClassification DefaultClassification { get; set; } = ProfileClassification.Unknown;
    public List<ClassificationRule> Rules { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Represents a single classification rule
/// </summary>
public class ClassificationRule
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }
    public List<RuleCondition> Conditions { get; set; } = new();
    public LogicalOperator LogicalOperator { get; set; } = LogicalOperator.And;
    public double WeightedThreshold { get; set; } = 0.5;
    public ProfileClassification ResultClassification { get; set; }
    public string? Reason { get; set; }
    public bool ContinueOnMatch { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Represents a rule condition
/// </summary>
public class RuleCondition
{
    public string Property { get; set; } = string.Empty;
    public ComparisonOperator Operator { get; set; }
    public object? Value { get; set; }
    public double Weight { get; set; } = 1.0;
}

/// <summary>
/// Logical operators for combining conditions
/// </summary>
public enum LogicalOperator
{
    And,
    Or,
    Not,
    Weighted
}

/// <summary>
/// Comparison operators for conditions
/// </summary>
public enum ComparisonOperator
{
    Unknown,
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains,
    StartsWith,
    EndsWith,
    IsNull,
    IsNotNull
}

/// <summary>
/// Context for rule evaluation
/// </summary>
public class RuleEvaluationContext
{
    public UserProfile Profile { get; set; } = new();
    public ProfileMetrics Metrics { get; set; } = new();
    public ActivityScoreResult? ActivityScore { get; set; }
    public DateTime EvaluationTime { get; set; }
    public Dictionary<string, object> CustomProperties { get; set; } = new();
}

/// <summary>
/// Result of rule evaluation
/// </summary>
public class RuleEvaluationResult
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public ProfileClassification Classification { get; set; } = ProfileClassification.Unknown;
    public string? MatchedRule { get; set; }
    public string ClassificationReason { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string RuleSetName { get; set; } = string.Empty;
    public string RuleSetVersion { get; set; } = string.Empty;
    public DateTime EvaluationTime { get; set; }
    public List<RuleResult> RuleResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Result of a single rule evaluation
/// </summary>
public class RuleResult
{
    public string RuleName { get; set; } = string.Empty;
    public string RuleDescription { get; set; } = string.Empty;
    public bool IsMatch { get; set; }
    public double Confidence { get; set; }
    public List<ConditionResult> ConditionResults { get; set; } = new();
    public string? Error { get; set; }
}

/// <summary>
/// Result of a single condition evaluation
/// </summary>
public class ConditionResult
{
    public string ConditionName { get; set; } = string.Empty;
    public bool IsMatch { get; set; }
    public string ActualValue { get; set; } = string.Empty;
    public string ExpectedOperator { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
}

/// <summary>
/// Result of rule set validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}