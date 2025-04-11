namespace GameMatcher.PlayerService;

using Data;
using Microsoft.EntityFrameworkCore;

public class PlayerService(AppDbContext context) : IPlayerService
{
    public async ValueTask<Player?> GetPlayer(int id)
    {
        var player = await context.Players.FindAsync(id);
        return player;
    }

    public Task<List<Player>> GetAllPlayers()
    {
        return context.Players.ToListAsync();
    }
    
    public void UpdatePlayerElo(Player player, int elo)
    {
        throw new NotImplementedException();
    }

    public async Task CreatePlayer(Player player)
    {
        context.Players.Add(player);
        await context.SaveChangesAsync();
    }
}