using GameMatcher.Data;
using GameMatcher.Models;
using Microsoft.EntityFrameworkCore;

namespace GameMatcher.Services;

public record PlayerRequest(string Name, string Surname = "", bool IsGoalkeeper = false);
public record AttendanceRequest(int PlayerId, AttendanceStatus Status);
public record GenerateRequest(int TeamCount = 2);
public record AssignmentRequest(int PlayerId, int TeamNumber, bool IsGoalkeeper = false, bool IsSubstitute = false);
public record SwapRequest(int PlayerAId, int PlayerBId);
public record ResultRequest(int ScoreA, int ScoreB);
public record EventRequest(DateTime ScheduledAt);

public sealed class WorkflowException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class GameMatcherService(AppDbContext db)
{
    public Task<List<Player>> Players() => db.Players.OrderBy(x => x.Surname).ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync();

    public async Task<Player> CreatePlayer(PlayerRequest request)
    {
        ValidatePlayer(request);
        var player = new Player { Name = request.Name.Trim(), Surname = request.Surname.Trim(), IsGoalkeeper = request.IsGoalkeeper };
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    public async Task<Player> UpdatePlayer(int id, PlayerRequest request)
    {
        ValidatePlayer(request);
        var player = await db.Players.FindAsync(id) ?? throw Missing("Player");
        player.Name = request.Name.Trim();
        player.Surname = request.Surname.Trim();
        player.IsGoalkeeper = request.IsGoalkeeper;
        await db.SaveChangesAsync();
        return player;
    }

    public async Task DeletePlayer(int id)
    {
        var player = await db.Players.FindAsync(id) ?? throw Missing("Player");
        if (await db.Attendances.AnyAsync(a => a.PlayerId == id && a.Status == AttendanceStatus.Attending &&
            (a.Event.Status == EventStatus.Draft || a.Event.Status == EventStatus.TeamsGenerated)))
            throw new WorkflowException("Remove this player from unfinished games before archiving them.", 409);
        player.IsActive = false;
        await db.SaveChangesAsync();
    }

    public async Task<Event> CreateEvent(DateTime scheduledAt)
    {
        ValidateDate(scheduledAt);
        var e = new Event { ScheduledAt = scheduledAt.ToUniversalTime() };
        db.Events.Add(e);
        await db.SaveChangesAsync();
        return e;
    }

    public Task<Event?> GetEvent(int id) => EventQuery().SingleOrDefaultAsync(x => x.Id == id);

    public Task<List<Event>> History() => EventQuery().OrderByDescending(x => x.ScheduledAt).ThenByDescending(x => x.Id).ToListAsync();

    public async Task<Event> UpdateEvent(int id, DateTime scheduledAt)
    {
        var e = await RequireEvent(id);
        EnsureEditable(e);
        ValidateDate(scheduledAt);
        e.ScheduledAt = scheduledAt.ToUniversalTime();
        await db.SaveChangesAsync();
        return e;
    }

    public async Task<Event> Cancel(int id)
    {
        var e = await RequireEvent(id);
        EnsureEditable(e);
        e.Status = EventStatus.Cancelled;
        await db.SaveChangesAsync();
        return e;
    }

    public async Task<Event> ResetTeams(int id)
    {
        var e = await RequireEvent(id);
        EnsureEditable(e);
        ReplaceAssignments(e, []);
        e.Status = EventStatus.Draft;
        await db.SaveChangesAsync();
        return e;
    }

    public async Task<Event> SetAttendance(int eventId, IEnumerable<AttendanceRequest> requests)
    {
        var e = await RequireEvent(eventId);
        EnsureEditable(e);
        var items = requests.ToList();
        if (items.Select(x => x.PlayerId).Distinct().Count() != items.Count || items.Any(x => !Enum.IsDefined(x.Status)))
            throw new WorkflowException("Attendance must contain unique player IDs and valid statuses.");
        var ids = items.Select(x => x.PlayerId).ToArray();
        if (await db.Players.CountAsync(x => ids.Contains(x.Id) && x.IsActive) != ids.Length)
            throw new WorkflowException("Attendance can only include active roster players.");
        var changed = items.Any(x => e.Attendances.FirstOrDefault(a => a.PlayerId == x.PlayerId)?.Status != x.Status);
        foreach (var item in items)
        {
            var attendance = e.Attendances.FirstOrDefault(a => a.PlayerId == item.PlayerId);
            if (attendance is null) e.Attendances.Add(new Attendance { PlayerId = item.PlayerId, Status = item.Status });
            else attendance.Status = item.Status;
        }
        if (changed)
        {
            ReplaceAssignments(e, []);
            e.Status = EventStatus.Draft;
        }
        await db.SaveChangesAsync();
        return await RequireEvent(eventId);
    }

    public async Task<Event> DesignateGoalkeepers(int eventId, IEnumerable<int> playerIds)
    {
        var e = await RequireEvent(eventId);
        EnsureEditable(e);
        var ids = playerIds.ToArray();
        if (ids.Length != 2 || ids.Distinct().Count() != 2)
            throw new WorkflowException("Select exactly two different goalkeepers.");
        var attending = Attending(e);
        if (ids.Any(id => attending.All(p => p.Id != id)))
            throw new WorkflowException("Only attending players can be designated as goalkeepers.");
        ReplaceAssignments(e, ids.Select(id => new TeamAssignment
        {
            PlayerId = id, IsGoalkeeper = true, IsSubstitute = !attending.Single(p => p.Id == id).IsGoalkeeper
        }));
        e.Status = EventStatus.Draft;
        await db.SaveChangesAsync();
        return await RequireEvent(eventId);
    }

    public async Task<Event> Generate(int eventId, int teamCount = 2)
    {
        var e = await RequireEvent(eventId);
        EnsureEditable(e);
        if (teamCount != 2) throw new WorkflowException("Games must have exactly two teams.");
        var players = Attending(e);
        if (players.Count < 4) throw new WorkflowException("Select at least four players, including two goalkeepers.");
        var keeperIds = e.Assignments.Where(x => x.IsGoalkeeper).Select(x => x.PlayerId).ToArray();
        if (keeperIds.Length == 0) keeperIds = players.Where(x => x.IsGoalkeeper).Select(x => x.Id).ToArray();
        if (keeperIds.Length != 2 || keeperIds.Distinct().Count() != 2 || keeperIds.Any(id => players.All(p => p.Id != id)))
            throw new WorkflowException("Confirm exactly two attending goalkeepers before generating teams.");
        var keepers = players.Where(p => keeperIds.Contains(p.Id)).OrderByDescending(p => p.Elo).ThenBy(p => p.Id).ToArray();
        var teams = new[] { new List<Player> { keepers[0] }, new List<Player> { keepers[1] } };
        var sizes = new[] { (players.Count + 1) / 2, players.Count / 2 };
        var sums = new[] { keepers[0].Elo, keepers[1].Elo };
        foreach (var player in players.Where(p => !keeperIds.Contains(p.Id)).OrderByDescending(p => p.Elo).ThenBy(p => p.Id))
        {
            var team = Enumerable.Range(0, 2).Where(i => teams[i].Count < sizes[i])
                .OrderBy(i => sums[i]).ThenBy(i => teams[i].Count).ThenBy(i => i).First();
            teams[team].Add(player);
            sums[team] += player.Elo;
        }
        ReplaceAssignments(e, teams.SelectMany((team, i) => team.Select(p => new TeamAssignment
        {
            PlayerId = p.Id, TeamNumber = i + 1, IsGoalkeeper = keeperIds.Contains(p.Id),
            IsSubstitute = keeperIds.Contains(p.Id) && !p.IsGoalkeeper
        })));
        e.Status = EventStatus.TeamsGenerated;
        await db.SaveChangesAsync();
        return await RequireEvent(eventId);
    }

    public async Task<Event> OverrideAssignments(int eventId, IEnumerable<AssignmentRequest> requests)
    {
        var e = await RequireEvent(eventId);
        EnsureEditable(e);
        if (e.Status != EventStatus.TeamsGenerated) throw new WorkflowException("Generate teams before adjusting them.", 409);
        var items = requests.ToList();
        ValidateTeams(e, items);
        var players = Attending(e);
        ReplaceAssignments(e, items.Select(x => new TeamAssignment
        {
            PlayerId = x.PlayerId, TeamNumber = x.TeamNumber, IsGoalkeeper = x.IsGoalkeeper,
            IsSubstitute = x.IsGoalkeeper && !players.Single(p => p.Id == x.PlayerId).IsGoalkeeper
        }));
        await db.SaveChangesAsync();
        return await RequireEvent(eventId);
    }

    public async Task<Event> Swap(int eventId, SwapRequest request)
    {
        var e = await RequireEvent(eventId);
        var items = e.Assignments.Select(x => new AssignmentRequest(x.PlayerId, x.TeamNumber, x.IsGoalkeeper)).ToList();
        var a = items.FindIndex(x => x.PlayerId == request.PlayerAId);
        var b = items.FindIndex(x => x.PlayerId == request.PlayerBId);
        if (a < 0 || b < 0 || a == b || items[a].TeamNumber == items[b].TeamNumber)
            throw new WorkflowException("Select two different players from opposing teams.");
        var teamA = items[a].TeamNumber;
        items[a] = items[a] with { TeamNumber = items[b].TeamNumber };
        items[b] = items[b] with { TeamNumber = teamA };
        return await OverrideAssignments(eventId, items);
    }

    public async Task<Event> RecordResult(int eventId, ResultRequest request)
    {
        var e = await RequireEvent(eventId);
        EnsureEditable(e);
        if (e.Status != EventStatus.TeamsGenerated) throw new WorkflowException("Generate valid teams before recording a result.", 409);
        if (request.ScoreA < 0 || request.ScoreB < 0) throw new WorkflowException("Scores cannot be negative.");
        ValidateTeams(e, e.Assignments.Select(x => new AssignmentRequest(x.PlayerId, x.TeamNumber, x.IsGoalkeeper)).ToList());
        await using var transaction = await db.Database.BeginTransactionAsync();
        e.Result = new MatchResult { EventId = eventId, ScoreA = request.ScoreA, ScoreB = request.ScoreB };
        e.Status = EventStatus.Played;
        await db.SaveChangesAsync();
        await RecomputeRatings();
        await transaction.CommitAsync();
        return e;
    }

    public async Task RecomputeRatings()
    {
        var players = await db.Players.ToListAsync();
        foreach (var p in players) p.Elo = 1500;
        var events = await EventQuery().Where(x => x.Status == EventStatus.Played && x.Result != null)
            .OrderBy(x => x.ScheduledAt).ThenBy(x => x.Id).ToListAsync();
        foreach (var e in events)
        {
            var a = e.Assignments.Where(x => x.TeamNumber == 1).ToList();
            var b = e.Assignments.Where(x => x.TeamNumber == 2).ToList();
            if (a.Count == 0 || b.Count == 0 || e.Assignments.Any(x => x.TeamNumber is not (1 or 2)))
                throw new WorkflowException($"Game {e.Id} has invalid historical teams; ratings cannot be recalculated.", 409);
            var expected = 1 / (1 + Math.Pow(10, (b.Average(x => x.Player.Elo) - a.Average(x => x.Player.Elo)) / 400));
            var actual = e.Result!.ScoreA == e.Result.ScoreB ? .5 : e.Result.ScoreA > e.Result.ScoreB ? 1 : 0;
            var change = (int)Math.Round(32 * (actual - expected));
            foreach (var p in a) p.Player.Elo += change;
            foreach (var p in b) p.Player.Elo -= change;
        }
        await db.SaveChangesAsync();
    }

    private IQueryable<Event> EventQuery() => db.Events.Include(x => x.Attendances).ThenInclude(x => x.Player)
        .Include(x => x.Assignments).ThenInclude(x => x.Player).Include(x => x.Result).AsSplitQuery();

    private async Task<Event> RequireEvent(int id) => await GetEvent(id) ?? throw Missing("Game");

    private static WorkflowException Missing(string entity) => new($"{entity} not found.", 404);

    private static void EnsureEditable(Event e)
    {
        if (e.Status is EventStatus.Played or EventStatus.Cancelled)
            throw new WorkflowException("Completed and cancelled games are locked.", 409);
    }

    private static void ValidatePlayer(PlayerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80 || request.Surname is null || request.Surname.Length > 80)
            throw new WorkflowException("Enter a name; first and last names must each be at most 80 characters.");
    }

    private static void ValidateDate(DateTime date)
    {
        if (date == default) throw new WorkflowException("Provide a game date and time.");
    }

    private static List<Player> Attending(Event e) => e.Attendances.Where(x => x.Status == AttendanceStatus.Attending)
        .Select(x => x.Player).DistinctBy(x => x.Id).ToList();

    private static void ValidateTeams(Event e, List<AssignmentRequest> items)
    {
        var ids = Attending(e).Select(p => p.Id).OrderBy(id => id);
        if (items.Count < 4 || items.Select(x => x.PlayerId).Distinct().Count() != items.Count ||
            !items.Select(x => x.PlayerId).OrderBy(id => id).SequenceEqual(ids) || items.Any(x => x.TeamNumber is not (1 or 2)))
            throw new WorkflowException("Assign every attending player exactly once to Team A or Team B.");
        if (Math.Abs(items.Count(x => x.TeamNumber == 1) - items.Count(x => x.TeamNumber == 2)) > 1)
            throw new WorkflowException("Team sizes must differ by at most one player.");
        if (items.Count(x => x.TeamNumber == 1 && x.IsGoalkeeper) != 1 || items.Count(x => x.TeamNumber == 2 && x.IsGoalkeeper) != 1)
            throw new WorkflowException("Each team must have exactly one goalkeeper.");
    }

    private void ReplaceAssignments(Event e, IEnumerable<TeamAssignment> assignments)
    {
        var replacement = assignments.ToList();
        db.TeamAssignments.RemoveRange(e.Assignments);
        e.Assignments.Clear();
        foreach (var assignment in replacement) e.Assignments.Add(assignment);
    }
}
