namespace GameMatcher.Models;

public record PlayerView(int Id, string Name, string Surname, bool IsGoalkeeper, int Elo, bool IsActive,
    int MatchesPlayed = 0, int Wins = 0, int Losses = 0, int Draws = 0, int GoalkeeperGoalsConceded = 0)
{
    public static PlayerView From(Player p, IEnumerable<Event>? history = null)
    {
        var games = (history ?? []).Where(e => e.Status == EventStatus.Played && e.Result != null)
            .SelectMany(e => e.Assignments.Where(a => a.PlayerId == p.Id).Select(a => new
            {
                Own = a.TeamNumber == 1 ? e.Result!.ScoreA : e.Result!.ScoreB,
                Opponent = a.TeamNumber == 1 ? e.Result!.ScoreB : e.Result!.ScoreA,
                a.IsGoalkeeper
            })).ToList();
        return new(p.Id, p.Name, p.Surname, p.IsGoalkeeper, p.Elo, p.IsActive, games.Count,
            games.Count(g => g.Own > g.Opponent), games.Count(g => g.Own < g.Opponent),
            games.Count(g => g.Own == g.Opponent), games.Where(g => g.IsGoalkeeper).Sum(g => g.Opponent));
    }
}

public record AttendanceView(int PlayerId, AttendanceStatus Status);
public record AssignmentView(int PlayerId, int TeamNumber, bool IsGoalkeeper, bool IsSubstitute, PlayerView Player);
public record ResultView(int ScoreA, int ScoreB, string Winner);
public record EventView(int Id, DateTime ScheduledAt, EventStatus Status, IEnumerable<AttendanceView> Attendances,
    IEnumerable<AssignmentView> Assignments, ResultView? Result)
{
    public static EventView From(Event e) => new(e.Id, e.ScheduledAt, e.Status,
        e.Attendances.Select(a => new AttendanceView(a.PlayerId, a.Status)),
        e.Assignments.Select(a => new AssignmentView(a.PlayerId, a.TeamNumber, a.IsGoalkeeper, a.IsSubstitute, PlayerView.From(a.Player))),
        e.Result is null ? null : new(e.Result.ScoreA, e.Result.ScoreB,
            e.Result.ScoreA == e.Result.ScoreB ? "Draw" : e.Result.ScoreA > e.Result.ScoreB ? "Team A" : "Team B"));
}
