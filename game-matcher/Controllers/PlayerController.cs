namespace GameMatcher.Controllers;

using Microsoft.AspNetCore.Mvc;
using PlayerService;

[ApiController]
[Route("api/[controller]")]
public class PlayerController(IPlayerService playerService) : ControllerBase
{
    // GET: api/player
    [HttpGet("players")]
    public async Task<ActionResult<IEnumerable<Player>>> GetAllPlayers()
    {
        var result = await playerService.GetAllPlayers();
        return Ok(result);
    }

    // GET: api/game/player/5
    [HttpGet("player/{id}")]
    public async Task<ActionResult<Player>> GetPlayer(int id)
    {
        var player = await playerService.GetPlayer(id);
        if (player == null)
        {
            return NotFound();
        }
        return Ok(player);
    }

    [HttpPost]
    public async Task<IResult> CreatePlayer(Player player)
    {
        await playerService.CreatePlayer(player);

        return Results.Ok();
    }
}