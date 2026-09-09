namespace Flagbit.Api.Contracts;

public sealed record CreateApiKeyRequest(string? Name);

public sealed record ApiKeyResponse(Guid Id, string Name, DateTimeOffset CreatedAt);

public sealed record CreatedApiKeyResponse(Guid Id, string Name, DateTimeOffset CreatedAt, string Key);
