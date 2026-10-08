using GameMatcher.Models;
using GameMatcher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameMatcher.Controllers;

[ApiController, Route("api/events")]
public class EventsController(GameMatcherService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(EventRequest request)
    {
        var e = await service.CreateEvent(request.ScheduledAt);
        return CreatedAtAction(nameof(Get), new { id = e.Id }, EventView.From(e));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => (await service.GetEvent(id)) is { } e
        ? Ok(EventView.From(e)) : NotFound(new ProblemDetails { Detail = "Game not found." });

    [HttpGet("history")]
    public async Task<IActionResult> History() => Ok((await service.History()).Select(EventView.From));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, EventRequest request) => Ok(EventView.From(await service.UpdateEvent(id, request.ScheduledAt)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id) => Ok(EventView.From(await service.Cancel(id)));

    [HttpPut("{id:int}/attendance")]
    public async Task<IActionResult> Attendance(int id, IEnumerable<AttendanceRequest> request) => Ok(EventView.From(await service.SetAttendance(id, request)));

    [HttpPut("{id:int}/goalkeepers")]
    public async Task<IActionResult> Goalkeepers(int id, IEnumerable<int> playerIds) => Ok(EventView.From(await service.DesignateGoalkeepers(id, playerIds)));

    [HttpPost("{id:int}/teams")]
    public async Task<IActionResult> Teams(int id, GenerateRequest request) => Ok(EventView.From(await service.Generate(id, request.TeamCount)));

    [HttpPut("{id:int}/teams")]
    public async Task<IActionResult> Override(int id, IEnumerable<AssignmentRequest> request) => Ok(EventView.From(await service.OverrideAssignments(id, request)));

    [HttpDelete("{id:int}/teams")]
    public async Task<IActionResult> ResetTeams(int id) => Ok(EventView.From(await service.ResetTeams(id)));

    [HttpPost("{id:int}/teams/swap")]
    public async Task<IActionResult> Swap(int id, SwapRequest request) => Ok(EventView.From(await service.Swap(id, request)));

    [HttpPost("{id:int}/result")]
    public async Task<IActionResult> Result(int id, ResultRequest request) => Ok(EventView.From(await service.RecordResult(id, request)));

    [HttpPost("recompute-ratings")]
    public async Task<IActionResult> Recompute()
    {
        await service.RecomputeRatings();
        return Ok((await service.Players()).Select(p => PlayerView.From(p)));
    }
}
