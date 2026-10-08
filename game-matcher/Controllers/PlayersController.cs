using GameMatcher.Models;
using GameMatcher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameMatcher.Controllers;

[ApiController, Route("api/players")]
public class PlayersController(GameMatcherService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var history = await service.History();
        return Ok((await service.Players()).Select(p => PlayerView.From(p, history)));
    }

    [HttpPost]
    public async Task<IActionResult> Post(PlayerRequest request)
    {
        var p = await service.CreatePlayer(request);
        return CreatedAtAction(nameof(GetById), new { id = p.Id }, PlayerView.From(p));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id) => (await service.Players()).FirstOrDefault(x => x.Id == id) is { } p
        ? Ok(PlayerView.From(p, await service.History())) : NotFound(new ProblemDetails { Detail = "Player not found." });

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Put(int id, PlayerRequest request) => Ok(PlayerView.From(await service.UpdatePlayer(id, request)));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await service.DeletePlayer(id);
        return NoContent();
    }
}
