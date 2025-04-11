
namespace GameMatcher.Controllers;

using Microsoft.AspNetCore.Mvc;
using GameService;

[ApiController]
[Route("api/[controller]")]
public class GameController(IGameService gameService) : ControllerBase
{
    // GET: api/player
    [HttpGet("players")]
    public async Task<ActionResult<IEnumerable<Player>>> GetAllGames()
    {
        var result = gameService.GetAllGames();
        return Ok(result);
    }

}