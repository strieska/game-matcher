using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using GameMatcher.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GameMatcher.Tests;

public class ApiTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly string password;
        public Factory(string password = "integration-test-only")
        {
            this.password = password;
            connection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new System.Collections.Generic.Dictionary<string, string?> { ["Organizer:Password"] = password }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) connection.Dispose();
        }
    }

    private record Session(bool IsOrganizer, string CsrfToken);
    private record PlayerResponse(int Id, string Name, int Elo);
    private record EventResponse(int Id, string Status);

    [Fact]
    public async Task Public_reads_work_all_writes_require_auth_and_csrf_and_json_has_no_cycles()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/events/history")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/players")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        foreach (var (method, path, body) in new[]
        {
            ("POST", "/api/players", "{}"), ("PUT", "/api/players/1", "{}"), ("DELETE", "/api/players/1", ""),
            ("POST", "/api/events", "{}"), ("PUT", "/api/events/1", "{}"),
            ("PUT", "/api/events/1/attendance", "[]"), ("PUT", "/api/events/1/goalkeepers", "[]"),
            ("POST", "/api/events/1/teams", "{}"), ("PUT", "/api/events/1/teams", "[]"),
            ("DELETE", "/api/events/1/teams", ""), ("POST", "/api/events/1/teams/swap", "{}"),
            ("POST", "/api/events/1/result", "{}"), ("POST", "/api/events/1/cancel", "{}"),
            ("POST", "/api/events/recompute-ratings", "")
        })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
        }
        var session = (await client.GetFromJsonAsync<Session>("/api/auth/session"))!;
        Assert.False(session.IsOrganizer);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { password = "integration-test-only" })).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.CsrfToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { password = "integration-test-only" })).StatusCode);
        session = (await client.GetFromJsonAsync<Session>("/api/auth/session"))!;
        Assert.True(session.IsOrganizer);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/players", new { name = "No token" })).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.CsrfToken);
        var players = new int[5];
        for (var i = 0; i < players.Length; i++)
        {
            var response = await client.PostAsJsonAsync("/api/players", new { name = $"Player {i}", isGoalkeeper = i < 2, elo = 4000 });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var player = (await response.Content.ReadFromJsonAsync<PlayerResponse>())!;
            Assert.Equal(1500, player.Elo);
            players[i] = player.Id;
        }
        var created = await client.PostAsJsonAsync("/api/events", new { scheduledAt = DateTime.UtcNow });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var game = (await created.Content.ReadFromJsonAsync<EventResponse>())!;
        var attendance = Array.ConvertAll(players, id => new { playerId = id, status = "Attending" });
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/events/{game.Id}/attendance", attendance)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/events/{game.Id}/teams", new { teamCount = 2 })).StatusCode);
        var teams = await client.GetStringAsync($"/api/events/{game.Id}");
        Assert.Contains("\"assignments\"", teams);
        Assert.DoesNotContain("\"event\":", teams);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/events/{game.Id}/result", new { scoreA = 2, scoreB = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/events/{game.Id}/result", new { scoreA = 4, scoreB = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/players/{players[0]}")).StatusCode);
        Assert.Contains("\"scoreA\":2", await client.GetStringAsync("/api/events/history"));
    }

    [Fact]
    public async Task Login_limits_attempts_to_five_per_minute()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var session = (await client.GetFromJsonAsync<Session>("/api/auth/session"))!;
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.CsrfToken);
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { password = "wrong" })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { password = "integration-test-only" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Contains("one minute", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Missing_password_disables_login_without_disabling_public_views()
    {
        using var factory = new Factory("");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var session = (await client.GetFromJsonAsync<Session>("/api/auth/session"))!;
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.CsrfToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/auth/login", new { password = "anything" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/events/history")).StatusCode);
    }
}
