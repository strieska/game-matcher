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

public class WorkflowTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private AppDbContext db = null!;
    private GameMatcherService service = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        service = new GameMatcherService(db);
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private async Task<(Event Event, Player[] Players)> Setup(int count = 8, int keepers = 2)
    {
        var players = Enumerable.Range(0, count).Select(i => new Player
        {
            Name = $"P{i}", Elo = 1000 + i * 100, IsGoalkeeper = i < keepers
        }).ToArray();
        db.Players.AddRange(players);
        await db.SaveChangesAsync();
        var e = await service.CreateEvent(DateTime.UtcNow);
        await service.SetAttendance(e.Id, players.Select(p => new AttendanceRequest(p.Id, AttendanceStatus.Attending)));
        return (await service.GetEvent(e.Id) ?? throw new InvalidOperationException(), players);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    public async Task Generation_preserves_attendance_sizes_and_one_keeper_per_team(int count)
    {
        var (e, players) = await Setup(count);
        var result = await service.Generate(e.Id);
        var assignments = result.Assignments.ToList();
        Assert.Equal(count, assignments.Count);
        Assert.Equal(count, assignments.Select(a => a.PlayerId).Distinct().Count());
        Assert.Equal((count + 1) / 2, assignments.Count(a => a.TeamNumber == 1));
        Assert.Equal(count / 2, assignments.Count(a => a.TeamNumber == 2));
        Assert.Equal(1, assignments.Count(a => a.TeamNumber == 1 && a.IsGoalkeeper));
        Assert.Equal(1, assignments.Count(a => a.TeamNumber == 2 && a.IsGoalkeeper));
        Assert.Equal(EventStatus.TeamsGenerated, result.Status);
        if (count % 2 == 0)
        {
            var sums = assignments.GroupBy(a => a.TeamNumber).Select(g => g.Sum(a => players.Single(p => p.Id == a.PlayerId).Elo)).ToArray();
            Assert.InRange(Math.Abs(sums[0] - sums[1]), 0, 400);
        }
        var first = assignments.OrderBy(a => a.PlayerId).Select(a => (a.PlayerId, a.TeamNumber)).ToArray();
        await service.Generate(e.Id);
        Assert.Equal(first, e.Assignments.OrderBy(a => a.PlayerId).Select(a => (a.PlayerId, a.TeamNumber)).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Generation_requires_exactly_two_confirmed_keepers(int keepers)
    {
        var (e, _) = await Setup(8, keepers);
        await Assert.ThrowsAsync<WorkflowException>(() => service.Generate(e.Id));
        Assert.Equal(EventStatus.Draft, e.Status);
        Assert.Empty(e.Assignments);
    }

    [Fact]
    public async Task Substitute_designation_survives_generation_and_counts_in_elo_and_stats()
    {
        var (e, players) = await Setup(8, 1);
        await service.DesignateGoalkeepers(e.Id, new[] { players[0].Id, players[1].Id });
        await service.Generate(e.Id);
        var substitute = e.Assignments.Single(a => a.PlayerId == players[1].Id);
        Assert.True(substitute.IsGoalkeeper);
        Assert.True(substitute.IsSubstitute);
        Assert.False(players[1].IsGoalkeeper);
        await service.RecordResult(e.Id, new ResultRequest(3, 1));
        Assert.Equal(substitute.TeamNumber == 1 ? 1516 : 1484, players[1].Elo);
        var stats = PlayerView.From(players[1], new[] { e });
        Assert.Equal(1, stats.MatchesPlayed);
        Assert.Equal(substitute.TeamNumber == 1 ? 1 : 3, stats.GoalkeeperGoalsConceded);
    }

    [Fact]
    public async Task Designation_rejects_absent_duplicate_or_missing_keepers()
    {
        var (e, players) = await Setup();
        await Assert.ThrowsAsync<WorkflowException>(() => service.DesignateGoalkeepers(e.Id, new[] { players[0].Id }));
        await Assert.ThrowsAsync<WorkflowException>(() => service.DesignateGoalkeepers(e.Id, new[] { players[0].Id, players[0].Id }));
        await Assert.ThrowsAsync<WorkflowException>(() => service.DesignateGoalkeepers(e.Id, new[] { players[0].Id, int.MaxValue }));
        Assert.Empty(e.Assignments);
    }

    [Fact]
    public async Task Attendance_invalidates_teams_and_rejects_invalid_players_atomically()
    {
        var (e, players) = await Setup();
        await service.Generate(e.Id);
        await Assert.ThrowsAsync<WorkflowException>(() => service.SetAttendance(e.Id, new[]
        {
            new AttendanceRequest(players[0].Id, AttendanceStatus.Absent),
            new AttendanceRequest(int.MaxValue, AttendanceStatus.Attending)
        }));
        Assert.Equal(EventStatus.TeamsGenerated, e.Status);
        Assert.Equal(AttendanceStatus.Attending, e.Attendances.Single(a => a.PlayerId == players[0].Id).Status);
        await service.SetAttendance(e.Id, new[] { new AttendanceRequest(players[2].Id, AttendanceStatus.Absent) });
        Assert.Equal(EventStatus.Draft, e.Status);
        Assert.Empty(e.Assignments);
    }

    [Fact]
    public async Task Overrides_validate_entire_split_and_swaps_preserve_keepers()
    {
        var (e, _) = await Setup();
        await service.Generate(e.Id);
        var original = e.Assignments.Select(a => new AssignmentRequest(a.PlayerId, a.TeamNumber, a.IsGoalkeeper)).ToArray();
        await Assert.ThrowsAsync<WorkflowException>(() => service.OverrideAssignments(e.Id, original.Skip(1)));
        await Assert.ThrowsAsync<WorkflowException>(() => service.OverrideAssignments(e.Id, original.Select(a => a with { TeamNumber = 1 })));
        await Assert.ThrowsAsync<WorkflowException>(() => service.OverrideAssignments(e.Id, original.Select(a => a with { IsGoalkeeper = false })));
        await Assert.ThrowsAsync<WorkflowException>(() => service.OverrideAssignments(e.Id, original.Select(a => a with { TeamNumber = 3 })));
        var a = original.First(x => x.TeamNumber == 1 && !x.IsGoalkeeper);
        var b = original.First(x => x.TeamNumber == 2 && !x.IsGoalkeeper);
        await service.Swap(e.Id, new SwapRequest(a.PlayerId, b.PlayerId));
        Assert.Equal(2, e.Assignments.Single(x => x.PlayerId == a.PlayerId).TeamNumber);
        Assert.Equal(1, e.Assignments.Single(x => x.PlayerId == b.PlayerId).TeamNumber);
        var goalkeeper = e.Assignments.First(x => x.TeamNumber == 1 && x.IsGoalkeeper);
        await Assert.ThrowsAsync<WorkflowException>(() => service.Swap(e.Id, new SwapRequest(goalkeeper.PlayerId, a.PlayerId)));
    }

    [Fact]
    public async Task Result_supports_draw_and_locks_all_game_mutations()
    {
        var (e, players) = await Setup();
        await service.Generate(e.Id);
        await service.RecordResult(e.Id, new ResultRequest(1, 1));
        Assert.All(players, p => Assert.Equal(1500, p.Elo));
        Assert.Equal(EventStatus.Played, e.Status);
        await Assert.ThrowsAsync<WorkflowException>(() => service.RecordResult(e.Id, new ResultRequest(2, 1)));
        await Assert.ThrowsAsync<WorkflowException>(() => service.Generate(e.Id));
        await Assert.ThrowsAsync<WorkflowException>(() => service.SetAttendance(e.Id, new[] { new AttendanceRequest(players[0].Id, AttendanceStatus.Absent) }));
        await Assert.ThrowsAsync<WorkflowException>(() => service.DesignateGoalkeepers(e.Id, new[] { players[0].Id, players[1].Id }));
        await Assert.ThrowsAsync<WorkflowException>(() => service.ResetTeams(e.Id));
        await Assert.ThrowsAsync<WorkflowException>(() => service.UpdateEvent(e.Id, DateTime.UtcNow));
        await Assert.ThrowsAsync<WorkflowException>(() => service.Cancel(e.Id));
        var assignments = e.Assignments.Select(a => new AssignmentRequest(a.PlayerId, a.TeamNumber, a.IsGoalkeeper));
        await Assert.ThrowsAsync<WorkflowException>(() => service.OverrideAssignments(e.Id, assignments));
        Assert.Equal(1, e.Result!.ScoreA);
        Assert.Equal(1, e.Result.ScoreB);
    }

    [Fact]
    public async Task Result_rejects_draft_negative_scores_and_cancelled_games()
    {
        var (e, _) = await Setup();
        await Assert.ThrowsAsync<WorkflowException>(() => service.RecordResult(e.Id, new ResultRequest(1, 0)));
        await service.Generate(e.Id);
        await Assert.ThrowsAsync<WorkflowException>(() => service.RecordResult(e.Id, new ResultRequest(-1, 0)));
        await service.Cancel(e.Id);
        await Assert.ThrowsAsync<WorkflowException>(() => service.RecordResult(e.Id, new ResultRequest(1, 0)));
        Assert.Null(e.Result);
    }

    [Fact]
    public async Task Replay_is_chronological_idempotent_and_includes_every_participant()
    {
        var (e, players) = await Setup();
        await service.Generate(e.Id);
        await service.RecordResult(e.Id, new ResultRequest(2, 0));
        var later = await service.CreateEvent(e.ScheduledAt.AddDays(7));
        await service.SetAttendance(later.Id, players.Select(p => new AttendanceRequest(p.Id, AttendanceStatus.Attending)));
        await service.Generate(later.Id);
        await service.RecordResult(later.Id, new ResultRequest(0, 3));
        var ratings = players.Select(p => p.Elo).ToArray();
        Assert.Equal(0, players.Sum(p => p.Elo - 1500));
        await service.RecomputeRatings();
        Assert.Equal(ratings, players.Select(p => p.Elo).ToArray());
        Assert.All(players, p => Assert.Equal(2, PlayerView.From(p, new[] { e, later }).MatchesPlayed));
    }

    [Fact]
    public async Task Player_edits_preserve_earned_elo_and_archiving_preserves_history()
    {
        var p = await service.CreatePlayer(new PlayerRequest("New player"));
        Assert.Equal(1500, p.Elo);
        p.Elo = 1600;
        await db.SaveChangesAsync();
        await service.UpdatePlayer(p.Id, new PlayerRequest("Renamed"));
        Assert.Equal(1600, p.Elo);
        await service.DeletePlayer(p.Id);
        Assert.False(p.IsActive);
        Assert.Contains(p, await service.Players());
        var e = await service.CreateEvent(DateTime.UtcNow);
        await Assert.ThrowsAsync<WorkflowException>(() => service.SetAttendance(e.Id, new[] { new AttendanceRequest(p.Id, AttendanceStatus.Attending) }));
    }

    [Fact]
    public async Task Reset_keeps_attendance_but_discards_teams_and_archive_rejects_active_participants()
    {
        var (e, players) = await Setup();
        await service.Generate(e.Id);
        await Assert.ThrowsAsync<WorkflowException>(() => service.DeletePlayer(players[0].Id));
        await service.ResetTeams(e.Id);
        Assert.Equal(8, e.Attendances.Count(a => a.Status == AttendanceStatus.Attending));
        Assert.Empty(e.Assignments);
        Assert.Equal(EventStatus.Draft, e.Status);
    }
}
