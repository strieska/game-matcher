namespace GameMatcher.Models
{
    public class Game
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }

        public int TeamAId { get; set; }
        public Team TeamA { get; set; } = null!;

        public int TeamBId { get; set; }
        public Team TeamB { get; set; } = null!;

        public int Outcome { get; set; } // 0 = draw, 1 = team A wins, 2 = team B wins
    }
}