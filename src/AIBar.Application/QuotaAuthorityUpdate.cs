using AIBar.Domain;

namespace AIBar.Application;

public sealed record QuotaAuthorityUpdate(QuotaRefreshState State, long EventSequence, long RetrievalGeneration);
