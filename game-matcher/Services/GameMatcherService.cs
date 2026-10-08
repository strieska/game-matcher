using GameMatcher.Data;
using GameMatcher.Models;
using Microsoft.EntityFrameworkCore;

namespace GameMatcher.Services;

public record PlayerRequest(string Name, string Surname, bool IsGoalkeeper = false, int Elo = 1500);
public record AttendanceRequest(int PlayerId, AttendanceStatus Status);
public record GenerateRequest(int TeamCount = 2);
public record AssignmentRequest(int PlayerId, int TeamNumber, bool IsGoalkeeper = false, bool IsSubstitute = false);
public record SwapRequest(int PlayerAId, int PlayerBId);
public record ResultRequest(int ScoreA, int ScoreB);

public sealed class GameMatcherService(AppDbContext db)
{
    public Task<List<Player>> Players() => db.Players.OrderBy(x => x.Surname).ThenBy(x => x.Name).ToListAsync();

    public async Task<Player?> CreatePlayer(PlayerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Surname)) return null;
        var player = new Player { Name = request.Name.Trim(), Surname = request.Surname.Trim(),
            IsGoalkeeper = request.IsGoalkeeper, Elo = request.Elo is >= 0 and <= 4000 ? request.Elo : 1500 };
        db.Players.Add(player); await db.SaveChangesAsync(); return player;
    }

    public async Task<Player?> UpdatePlayer(int id, PlayerRequest request)
    {
        var player = await db.Players.FindAsync(id);
        if (player is null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Surname)) return null;
        player.Name = request.Name.Trim(); player.Surname = request.Surname.Trim(); player.IsGoalkeeper = request.IsGoalkeeper;
        player.Elo = request.Elo is >= 0 and <= 4000 ? request.Elo : player.Elo;
        await db.SaveChangesAsync(); return player;
    }

    public async Task<bool> DeletePlayer(int id)
    {
        var player = await db.Players.FindAsync(id); if (player is null) return false;
        player.IsActive = false; await db.SaveChangesAsync(); return true;
    }

    public async Task<Event> CreateEvent(DateTime scheduledAt)
    {
        var e = new Event { ScheduledAt = scheduledAt.ToUniversalTime() }; db.Events.Add(e); await db.SaveChangesAsync(); return e;
    }

    public Task<Event?> GetEvent(int id) => db.Events.Include(x => x.Attendances).ThenInclude(x => x.Player)
        .Include(x => x.Assignments).ThenInclude(x => x.Player).Include(x => x.Result).SingleOrDefaultAsync(x => x.Id == id);

    public Task<List<Event>> History() => db.Events.Include(x => x.Assignments).ThenInclude(x => x.Player)
        .Include(x => x.Result).OrderByDescending(x => x.ScheduledAt).ToListAsync();

    public async Task<(bool Found, string? Warning)> SetAttendance(int eventId, IEnumerable<AttendanceRequest> requests)
    {
        if (!await db.Events.AnyAsync(x => x.Id == eventId)) return (false, null);
        foreach (var request in requests)
        {
            if (!await db.Players.AnyAsync(x => x.Id == request.PlayerId && x.IsActive)) continue;
            var attendance = await db.Attendances.FindAsync(eventId, request.PlayerId);
            if (attendance is null) db.Attendances.Add(new Attendance { EventId = eventId, PlayerId = request.PlayerId, Status = request.Status });
            else attendance.Status = request.Status;
        }
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Found, string? Warning)> DesignateGoalkeepers(int eventId, IEnumerable<int> playerIds)
    {
        var e = await GetEvent(eventId); if (e is null) return (false, null);
        var ids = playerIds.Distinct().ToArray();
        if (ids.Any(id => !e.Attendances.Any(a => a.PlayerId == id && a.Status == AttendanceStatus.Attending)))
            return (true, "Only attending players can be designated as goalkeepers.");
        foreach (var assignment in e.Assignments) assignment.IsGoalkeeper = ids.Contains(assignment.PlayerId);
        foreach (var id in ids.Where(id => !e.Assignments.Any(a => a.PlayerId == id)))
            e.Assignments.Add(new TeamAssignment { PlayerId = id, IsGoalkeeper = true, IsSubstitute = true });
        await db.SaveChangesAsync();
        return (true, ids.Length == 2 ? null : $"Exactly two goalkeepers are recommended; {ids.Length} designated.");
    }

    public async Task<(Event? Event, string? Warning, string? Error)> Generate(int eventId, int teamCount)
    {
        var e = await GetEvent(eventId); if (e is null) return (null, null, "Event not found.");
        if (teamCount < 2 || teamCount > 4) return (e, null, "Team count must be between 2 and 4.");
        var players = e.Attendances.Where(x => x.Status == AttendanceStatus.Attending).Select(x => x.Player).DistinctBy(x => x.Id)
            .OrderByDescending(x => x.Elo).ToList();
        if (players.Count < teamCount) return (e, null, "Not enough attending players.");
        var sizes = Enumerable.Range(0, teamCount).Select(i => players.Count / teamCount + (i < players.Count % teamCount ? 1 : 0)).ToArray();
        var teams = Enumerable.Range(0, teamCount).Select(_ => new List<Player>()).ToArray();
        var sums = new int[teamCount];
        foreach (var player in players)
        {
            var team = Enumerable.Range(0, teamCount).Where(i => teams[i].Count < sizes[i])
                .OrderBy(i => sums[i]).ThenBy(i => teams[i].Count).First();
            teams[team].Add(player); sums[team] += player.Elo;
        }
        db.TeamAssignments.RemoveRange(e.Assignments);
        for (var i = 0; i < teams.Length; i++)
            foreach (var player in teams[i])
                db.TeamAssignments.Add(new TeamAssignment { EventId = eventId, TeamNumber = i + 1, PlayerId = player.Id, IsGoalkeeper = player.IsGoalkeeper });
        e.Status = EventStatus.TeamsGenerated; await db.SaveChangesAsync();
        var goalkeepers = players.Count(x => x.IsGoalkeeper);
        return (await GetEvent(eventId), goalkeepers == 2 ? null : $"Exactly two goalkeeper-flagged attending players are recommended; found {goalkeepers}.", null);
    }

    public async Task<bool> OverrideAssignments(int eventId, IEnumerable<AssignmentRequest> requests)
    {
        var e = await db.Events.FindAsync(eventId); if (e is null) return false;
        var items = requests.ToList(); if (!items.Any() || items.Any(x => x.TeamNumber < 1 || x.TeamNumber > 4)) return false;
        var ids = items.Select(x => x.PlayerId).ToArray();
        if (ids.Length != ids.Distinct().Count() || await db.Players.CountAsync(x => ids.Contains(x.Id)) != ids.Length) return false;
        db.TeamAssignments.RemoveRange(db.TeamAssignments.Where(x => x.EventId == eventId));
        db.TeamAssignments.AddRange(items.Select(x => new TeamAssignment { EventId = eventId, PlayerId = x.PlayerId, TeamNumber = x.TeamNumber, IsGoalkeeper = x.IsGoalkeeper, IsSubstitute = x.IsSubstitute }));
        e.Status = EventStatus.TeamsGenerated; await db.SaveChangesAsync(); return true;
    }

    public async Task<bool> Swap(int eventId, SwapRequest request)
    {
        var assignments = await db.TeamAssignments.Where(x => x.EventId == eventId &&
            (x.PlayerId == request.PlayerAId || x.PlayerId == request.PlayerBId)).ToListAsync();
        if (assignments.Count != 2 || assignments[0].PlayerId == assignments[1].PlayerId ||
            assignments[0].TeamNumber == assignments[1].TeamNumber) return false;
        (assignments[0].TeamNumber, assignments[1].TeamNumber) = (assignments[1].TeamNumber, assignments[0].TeamNumber);
        await db.SaveChangesAsync(); return true;
    }

    public async Task<bool> RecordResult(int eventId, ResultRequest request)
    {
        if (request.ScoreA < 0 || request.ScoreB < 0) return false;
        var e = await db.Events.Include(x => x.Result).Include(x => x.Assignments).ThenInclude(x => x.Player).SingleOrDefaultAsync(x => x.Id == eventId);
        if (e is null || !e.Assignments.Any(x => !x.IsSubstitute)) return false;
        e.Result ??= new MatchResult { EventId = eventId }; e.Result.ScoreA = request.ScoreA; e.Result.ScoreB = request.ScoreB;
        e.Status = EventStatus.Played; await db.SaveChangesAsync(); await RecomputeRatings(); return true;
    }

    public async Task RecomputeRatings()
    {
        var players = await db.Players.ToListAsync(); foreach (var p in players) p.Elo = 1500;
        var events = await db.Events.Include(x => x.Result).Include(x => x.Assignments).ThenInclude(x => x.Player)
            .Where(x => x.Status == EventStatus.Played && x.Result != null).OrderBy(x => x.ScheduledAt).ToListAsync();
        foreach (var e in events)
        {
            var a = e.Assignments.Where(x => x.TeamNumber == 1 && !x.IsSubstitute).ToList();
            var b = e.Assignments.Where(x => x.TeamNumber != 1 && !x.IsSubstitute).ToList();
            if (!a.Any() || !b.Any()) continue;
            var ra = a.Average(x => x.Player.Elo); var rb = b.Average(x => x.Player.Elo);
            var actual = e.Result!.ScoreA == e.Result.ScoreB ? .5 : e.Result.ScoreA > e.Result.ScoreB ? 1 : 0;
            var expected = 1 / (1 + Math.Pow(10, (rb - ra) / 400));
            foreach (var p in a) p.Player.Elo += (int)Math.Round(32 * (actual - expected));
            foreach (var p in b) p.Player.Elo += (int)Math.Round(32 * ((1 - actual) - (1 - expected)));
        }
        await db.SaveChangesAsync();
    }
}
