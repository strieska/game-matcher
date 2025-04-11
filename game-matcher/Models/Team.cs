namespace GameMatcher.Models;

public class Team
{
    public int Id { get; set; }
    public ICollection<Player> Players { get; set; } = new List<Player>();
}

