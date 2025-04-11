namespace GameMatcher.GameService;

using GameMatcher.Data;
using GameMatcher.Models;
using Microsoft.EntityFrameworkCore;

public class GameService(AppDbContext context) : IGameService
{
    public IEnumerable<Game> GetAllGames()
    {
        throw new NotImplementedException();
    }
}