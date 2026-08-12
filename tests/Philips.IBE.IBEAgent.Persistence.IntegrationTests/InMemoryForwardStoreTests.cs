using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Persistence.IntegrationTests;

public sealed class InMemoryForwardStoreTests
{
    private static InMemoryForwardStore CreateStore() => new(new NullDataProtector());

    [Fact]
    public async Task StoreAsync_then_FetchDueAsync_returns_pending_entry()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "hello");

        await store.StoreAsync(ctx, outputId: 42, error: "boom", CancellationToken.None);

        var due = await store.FetchDueAsync(10, CancellationToken.None);

        Assert.Single(due);
        Assert.Equal(42, due[0].OutputId);
        Assert.Equal(ForwardStatus.Pending, due[0].Status);
        Assert.Equal("boom", due[0].LastError);
    }

    [Fact]
    public async Task ResolveAsync_removes_the_matching_row()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "hello");
        await store.StoreAsync(ctx, outputId: 1, error: null, CancellationToken.None);

        await store.ResolveAsync(ctx, outputId: 1, CancellationToken.None);

        var due = await store.FetchDueAsync(10, CancellationToken.None);
        Assert.Empty(due);
    }

    [Fact]
    public async Task RescheduleAsync_defers_next_attempt_so_row_is_not_yet_due()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "hello");
        await store.StoreAsync(ctx, outputId: 1, error: null, CancellationToken.None);
        var entry = (await store.FetchDueAsync(10, CancellationToken.None)).Single();

        await store.RescheduleAsync(entry.Id, attempts: 1, DateTimeOffset.UtcNow.AddMinutes(5), "transient", CancellationToken.None);

        var due = await store.FetchDueAsync(10, CancellationToken.None);
        Assert.Empty(due);
    }

    [Fact]
    public async Task FetchDueAsync_leases_entries_so_concurrent_fetches_do_not_duplicate_rows()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "hello");
        await store.StoreAsync(ctx, outputId: 1, error: null, CancellationToken.None);

        var first = await store.FetchDueAsync(10, CancellationToken.None);
        var second = await store.FetchDueAsync(10, CancellationToken.None);

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Fact]
    public async Task RescheduleAsync_releases_the_lease_for_future_due_fetches()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "hello");
        await store.StoreAsync(ctx, outputId: 1, error: null, CancellationToken.None);
        var entry = (await store.FetchDueAsync(10, CancellationToken.None)).Single();

        await store.RescheduleAsync(entry.Id, attempts: 1, DateTimeOffset.UtcNow.AddMilliseconds(-1), "retry", CancellationToken.None);

        var due = await store.FetchDueAsync(10, CancellationToken.None);
        Assert.Single(due);
    }

    [Fact]
    public async Task ParkAsync_moves_entry_out_of_the_due_set_permanently()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "hello");
        await store.StoreAsync(ctx, outputId: 1, error: null, CancellationToken.None);
        var entry = (await store.FetchDueAsync(10, CancellationToken.None)).Single();

        await store.ParkAsync(entry.Id, "poison", CancellationToken.None);

        var due = await store.FetchDueAsync(10, CancellationToken.None);
        Assert.Empty(due);
    }

    [Fact]
    public async Task ListAsync_returns_parked_entries_for_manual_review()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "hello");
        await store.StoreAsync(ctx, outputId: 1, error: "boom", CancellationToken.None);
        var entry = (await store.FetchDueAsync(10, CancellationToken.None)).Single();
        await store.ParkAsync(entry.Id, "poison", CancellationToken.None);

        var parked = await store.ListAsync(ForwardStatus.Parked, max: 10, CancellationToken.None);

        var listed = Assert.Single(parked);
        Assert.Equal(entry.Id, listed.Id);
        Assert.Equal(ForwardStatus.Parked, listed.Status);
        Assert.Equal("poison", listed.LastError);
    }

    [Fact]
    public async Task RequeueAsync_moves_parked_entry_back_to_due_pending()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "hello");
        await store.StoreAsync(ctx, outputId: 1, error: "boom", CancellationToken.None);
        var entry = (await store.FetchDueAsync(10, CancellationToken.None)).Single();
        await store.ParkAsync(entry.Id, "poison", CancellationToken.None);

        var requeued = await store.RequeueAsync(entry.Id, CancellationToken.None);
        var due = await store.FetchDueAsync(10, CancellationToken.None);

        Assert.True(requeued);
        var dueEntry = Assert.Single(due);
        Assert.Equal(entry.Id, dueEntry.Id);
        Assert.Equal(ForwardStatus.Pending, dueEntry.Status);
        Assert.Null(dueEntry.LastError);
    }

    [Fact]
    public async Task DiscardAsync_removes_entry_from_manual_review_and_replay()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "hello");
        await store.StoreAsync(ctx, outputId: 1, error: "boom", CancellationToken.None);
        var entry = (await store.FetchDueAsync(10, CancellationToken.None)).Single();
        await store.ParkAsync(entry.Id, "poison", CancellationToken.None);

        var discarded = await store.DiscardAsync(entry.Id, "operator discarded", CancellationToken.None);
        var all = await store.ListAsync(status: null, max: 10, CancellationToken.None);

        Assert.True(discarded);
        Assert.Empty(all);
    }

    [Fact]
    public async Task Message_bytes_round_trip_through_the_configured_protector()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "payload-bytes");
        await store.StoreAsync(ctx, outputId: 7, error: null, CancellationToken.None);

        var entry = (await store.FetchDueAsync(10, CancellationToken.None)).Single();

        var envelope = System.Text.Json.JsonSerializer.Deserialize<TestEnvelope>(entry.Message.Span);
        Assert.NotNull(envelope);
        Assert.Equal("payload-bytes", System.Text.Encoding.UTF8.GetString(envelope!.Payload));
    }

    private sealed class TestEnvelope
    {
        public byte[] Payload { get; init; } = [];
    }
}
