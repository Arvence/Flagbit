namespace Flagbit.Api.Authentication;

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";
    public const string ManagementPolicy = "Management";
    public const string EvaluationPolicy = "Evaluation";

    public string ManagementKey { get; set; } = string.Empty;
    public string EvaluationKey { get; set; } = string.Empty;
}
