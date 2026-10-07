using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using SakuraFilter.Api.Services;
using Xunit;

namespace SakuraFilter.Api.Tests.Integration;

[Collection("PgSequential")]
[Trait("Category", "Integration")]
public class AuthTokenBroadcasterIntegrationTests : PgIntegrationTestBase
{
    [Fact]
    public async Task Notification_ReloadsTokenStore_AndStopsCleanly_Integration()
    {
        if (!IsEnabled) return;

        var reloadCount = 0;
        var reloadSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Mock<IAuthTokenStore>();
        store.SetupGet(x => x.Current).Returns("integration-test-token");
        store.Setup(x => x.ReloadFromDbAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref reloadCount) == 2)
                    reloadSignal.TrySetResult();
                return Task.CompletedTask;
            });

        var services = new ServiceCollection();
        services.AddSingleton(store.Object);
        await using var provider = services.BuildServiceProvider();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString
            })
            .Build();
        var status = new HostedServiceStatus(NullLogger<HostedServiceStatus>.Instance);
        await using var broadcaster = new AuthTokenBroadcaster(
            provider,
            NullLogger<AuthTokenBroadcaster>.Instance,
            config,
            status);

        await broadcaster.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilListeningAsync(broadcaster);
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_notify('auth_token_rotated', '{\"rotatedBy\":\"integration-test-1\"}'); SELECT pg_notify('auth_token_rotated', '{\"rotatedBy\":\"integration-test-2\"}')";
            await command.ExecuteNonQueryAsync();

            await reloadSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
            store.Verify(x => x.ReloadFromDbAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
        finally
        {
            await broadcaster.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntilListeningAsync(AuthTokenBroadcaster broadcaster)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!broadcaster.IsListening && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.True(broadcaster.IsListening, "AuthTokenBroadcaster 未在限定时间内开始监听");
    }
}
