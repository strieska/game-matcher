namespace GameMatcher.PlayerService;

public interface IPlayerService
{
    public ValueTask<Player?> GetPlayer(int id);
    public Task<List<Player>> GetAllPlayers();
    public void UpdatePlayerElo(Player player, int elo);
    public Task CreatePlayer(Player player);
}