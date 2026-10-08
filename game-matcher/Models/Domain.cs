namespace GameMatcher.Models;

public enum EventStatus { Draft, TeamsGenerated, Played, Cancelled }
public enum AttendanceStatus { Unknown, Attending, Absent }

public class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";
    public bool IsGoalkeeper { get; set; }
    public int Elo { get; set; } = 1500;
    public bool IsActive { get; set; } = true;
}

public class Event
{
    public int Id { get; set; }
    public DateTime ScheduledAt { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Draft;
    public ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();
    public ICollection<TeamAssignment> Assignments { get; set; } = new List<TeamAssignment>();
    public MatchResult? Result { get; set; }
}

public class Attendance
{
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public AttendanceStatus Status { get; set; }
}

public class TeamAssignment
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int TeamNumber { get; set; }
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public bool IsGoalkeeper { get; set; }
    public bool IsSubstitute { get; set; }
}

public class MatchResult
{
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int ScoreA { get; set; }
    public int ScoreB { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
