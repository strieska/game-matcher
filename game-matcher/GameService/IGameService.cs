namespace GameMatcher.GameService;

using GameMatcher.Models;

public interface IGameService
{
    public IEnumerable<Game> GetAllGames();
}