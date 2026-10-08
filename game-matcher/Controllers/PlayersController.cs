using GameMatcher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameMatcher.Controllers;

[ApiController, Route("api/players")]
public class PlayersController(GameMatcherService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Get() => Ok(await service.Players());
    [HttpPost] public async Task<IActionResult> Post(PlayerRequest request) => (await service.CreatePlayer(request)) is { } p ? CreatedAtAction(nameof(GetById), new { id = p.Id }, p) : BadRequest();
    [HttpGet("{id:int}")] public async Task<IActionResult> GetById(int id) => (await service.Players()).FirstOrDefault(x => x.Id == id) is { } p ? Ok(p) : NotFound();
    [HttpPut("{id:int}")] public async Task<IActionResult> Put(int id, PlayerRequest request) => (await service.UpdatePlayer(id, request)) is { } p ? Ok(p) : NotFound();
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id) => await service.DeletePlayer(id) ? NoContent() : NotFound();
}
