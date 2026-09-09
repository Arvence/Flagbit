namespace Flagbit.Infrastructure.Persistence.Entities;

internal sealed class EvaluationApiKeyEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string KeyHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
