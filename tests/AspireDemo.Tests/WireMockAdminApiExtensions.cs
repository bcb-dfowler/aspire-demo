using WireMock.Admin.Mappings;
using WireMock.Client;

namespace AspireDemo.Tests.Tests;

public static class WireMockAdminApiExtensions
{
    /// <summary>
    /// Posts <paramref name="mapping"/> to WireMock's admin API and returns a handle that
    /// deletes it again on disposal, so a test can scope a stub to its own lifetime with
    /// <c>await using</c> instead of leaking it into later tests.
    /// </summary>
    public static async Task<IAsyncDisposable> PostScopedMappingAsync(
        this IWireMockAdminApi adminApi,
        Action<MappingModelBuilder> mappingBuilder,
        CancellationToken cancellationToken = default)
    {
        var builder = new MappingModelBuilder();
        mappingBuilder(builder);
        var mapping = builder.Build();
        mapping.Guid ??= Guid.NewGuid();

        await adminApi.PostMappingAsync(mapping, cancellationToken);

        return new ScopedMapping(adminApi, mapping.Guid.Value);
    }

    private sealed class ScopedMapping(IWireMockAdminApi adminApi, Guid mappingGuid) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await adminApi.DeleteMappingAsync(mappingGuid);
        }
    }
}
