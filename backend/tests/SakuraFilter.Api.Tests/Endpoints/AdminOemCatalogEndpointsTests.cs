using System.Net;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SakuraFilter.Api.Endpoints;
using Xunit;

namespace SakuraFilter.Api.Tests.Endpoints;

public class AdminOemCatalogEndpointsTests
{
    [Fact]
    public async Task ProductDetailEndpoint_WithSlashInOem_StillMatchesProtectedRoute()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Noop")
                        .AddScheme<AuthenticationSchemeOptions, NoopAuthHandler>("Noop", _ => { });
                    services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireAuthenticatedUser()));
                    services.AddSingleton(new NpgsqlDataSourceBuilder(
                        "Host=localhost;Port=5432;Database=unused;Username=unused;Password=unused").Build());
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapAdminOemCatalogEndpoints());
                });
            })
            .StartAsync();

        var response = await host.GetTestClient().GetAsync("/api/admin/oem-catalog/products/AB%2F123");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class NoopAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public NoopAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }
}
