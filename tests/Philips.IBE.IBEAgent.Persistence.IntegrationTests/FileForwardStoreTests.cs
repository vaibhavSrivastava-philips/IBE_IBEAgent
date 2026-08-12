using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Persistence.IntegrationTests;

public sealed class FileForwardStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ibe-forward-store-tests", Guid.NewGuid().ToString("N"));

    private FileForwardStore CreateStore(TimeSpan? lease = null)
        => new(_directory, new NullDataProtector(), lease ?? TimeSpan.FromMinutes(5));

    [Fact]
    public async Task Stored_entries_survive_store_recreation()
    {
        var ctx = MessageContextBuilder.Create(payload: "durable-payload");
        await CreateStore().StoreAsync(ctx, outputId: 42, error: "boom", CancellationToken.None);

        var due = await CreateStore().FetchDueAsync(10, CancellationToken.None);

        var entry = Assert.Single(due);
        Assert.Equal(42, entry.OutputId);
        Assert.Equal("boom", entry.LastError);
        var envelope = System.Text.Json.JsonSerializer.Deserialize<TestEnvelope>(entry.Message.Span);
        Assert.NotNull(envelope);
        Assert.Equal("durable-payload", System.Text.Encoding.UTF8.GetString(envelope!.Payload));
    }

    [Fact]
    public async Task FetchDueAsync_leases_rows_across_store_instances()
    {
        var ctx = MessageContextBuilder.Create(payload: "hello");
        await CreateStore().StoreAsync(ctx, outputId: 1, error: null, CancellationToken.None);

        var first = await CreateStore().FetchDueAsync(10, CancellationToken.None);
        var second = await CreateStore().FetchDueAsync(10, CancellationToken.None);

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Fact]
    public async Task ResolveAsync_removes_matching_persisted_row()
    {
        var ctx = MessageContextBuilder.Create(payload: "hello");
        await CreateStore().StoreAsync(ctx, outputId: 1, error: null, CancellationToken.None);

        await CreateStore().ResolveAsync(ctx, outputId: 1, CancellationToken.None);

        var all = await CreateStore().ListAsync(status: null, max: 10, CancellationToken.None);
        Assert.Empty(all);
    }

    [Fact]
    public async Task RequeueAsync_and_DiscardAsync_support_manual_operations_after_restart()
    {
        var ctx = MessageContextBuilder.Create(payload: "hello");
        var store = CreateStore();
        await store.StoreAsync(ctx, outputId: 1, error: "boom", CancellationToken.None);
        var entry = (await store.FetchDueAsync(10, CancellationToken.None)).Single();
        await store.ParkAsync(entry.Id, "poison", CancellationToken.None);

        var restarted = CreateStore();
        Assert.True(await restarted.RequeueAsync(entry.Id, CancellationToken.None));
        var due = await restarted.FetchDueAsync(10, CancellationToken.None);
        Assert.Single(due);

        Assert.True(await restarted.DiscardAsync(entry.Id, "operator discarded", CancellationToken.None));
        Assert.Empty(await restarted.ListAsync(status: null, max: 10, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class TestEnvelope
    {
        public byte[] Payload { get; init; } = [];
    }
}
