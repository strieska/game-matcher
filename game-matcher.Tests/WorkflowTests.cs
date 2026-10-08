using System;
using System.Linq;
using System.Threading.Tasks;
using GameMatcher.Data;
using GameMatcher.Models;
using GameMatcher.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameMatcher.Tests;

public class WorkflowTests
{
    private static (AppDbContext Db, SqliteConnection Connection) CreateDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return (db, connection);
    }

    [Fact]
    public async Task Generation_respects_team_sizes_and_balances_rating()
    {
        var (db, connection) = CreateDb();
        try
        {
            var players = Enumerable.Range(0, 8).Select(i => new Player { Name = $"P{i}", Surname = "Test", Elo = 1000 + i * 100 }).ToList();
            db.Players.AddRange(players);
            var e = new Event { ScheduledAt = DateTime.UtcNow };
            db.Events.Add(e);
            await db.SaveChangesAsync();
            db.Attendances.AddRange(players.Select(p => new Attendance { EventId = e.Id, PlayerId = p.Id, Status = AttendanceStatus.Attending }));
            await db.SaveChangesAsync();

            var service = new GameMatcherService(db);
            var result = await service.Generate(e.Id, 2);

            Assert.Null(result.Error);
            var assignments = await db.TeamAssignments.ToListAsync();
            Assert.Equal(4, assignments.Count(x => x.TeamNumber == 1));
            Assert.Equal(4, assignments.Count(x => x.TeamNumber == 2));
            var sums = assignments.GroupBy(x => x.TeamNumber).Select(g => g.Sum(x => players.Single(p => p.Id == x.PlayerId).Elo)).ToArray();
            Assert.InRange(Math.Abs(sums[0] - sums[1]), 0, 400);
        }
        finally { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    [Fact]
    public async Task Result_recomputes_elo_and_supports_draw()
    {
        var (db, connection) = CreateDb();
        try
        {
            var players = new[] { new Player { Name = "A", Surname = "A" }, new Player { Name = "B", Surname = "B" } };
            db.Players.AddRange(players);
            var e = new Event { ScheduledAt = DateTime.UtcNow };
            db.Events.Add(e);
            await db.SaveChangesAsync();
            db.Attendances.AddRange(players.Select(p => new Attendance { EventId = e.Id, PlayerId = p.Id, Status = AttendanceStatus.Attending }));
            db.TeamAssignments.AddRange(new TeamAssignment { EventId = e.Id, PlayerId = players[0].Id, TeamNumber = 1 },
                new TeamAssignment { EventId = e.Id, PlayerId = players[1].Id, TeamNumber = 2 });
            await db.SaveChangesAsync();

            var service = new GameMatcherService(db);
            Assert.True(await service.RecordResult(e.Id, new ResultRequest(1, 1)));
            Assert.All(await db.Players.ToListAsync(), p => Assert.Equal(1500, p.Elo));
        }
        finally { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
