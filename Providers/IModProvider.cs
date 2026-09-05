using Bibliognost.Models;

namespace Bibliognost.Providers;

public interface IModProvider
{
    string Id { get; }
    string DisplayName { get; }
    bool SupportsAuthentication { get; }
    bool SupportsDirectDownloads { get; }
    Task<ProviderResult<IReadOnlyList<ModSummary>>> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken = default);
    Task<ProviderResult<ModDetails>> GetDetailsAsync(string remoteId, CancellationToken cancellationToken = default);
    Task<AuthenticationStatus> VerifyAuthenticationAsync(CancellationToken cancellationToken = default);
}
