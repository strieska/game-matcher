namespace GameMatcher.Data
{
    using Models;

    public static class DbInitializer
    {
        public static void Seed(AppDbContext context)
        {
            if (context.Players.Any()) return; // DB already seeded

            var players = new List<Player>
            {
                new Player { Name = "Alice", Surname = "Smith", Elo = 1400 },
                new Player { Name = "Bob", Surname = "Jones", Elo = 1350 },
                new Player { Name = "Charlie", Surname = "Brown", Elo = 1500 },
                new Player { Name = "Dana", Surname = "White", Elo = 1450 }
            };

            var team1 = new Team { Players = new List<Player> { players[0], players[1] } };
            var team2 = new Team { Players = new List<Player> { players[2], players[3] } };

            var match = new Game
            {
                Date = DateTime.UtcNow,
                Outcome = 1,
                TeamA = team1,
                TeamB = team2
            };

            context.Players.AddRange(players);
            context.Teams.AddRange(team1, team2);
            context.GameMatches.Add(match);

            context.SaveChanges();
        }
    }
}