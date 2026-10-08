using GameMatcher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameMatcher.Controllers;

[ApiController, Route("api/events")]
public class EventsController(GameMatcherService service) : ControllerBase
{
    [HttpPost] public async Task<IActionResult> Create(DateTime scheduledAt) => Ok(await service.CreateEvent(scheduledAt));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id) => (await service.GetEvent(id)) is { } e ? Ok(e) : NotFound();
    [HttpGet("history")] public async Task<IActionResult> History() => Ok(await service.History());
    [HttpPut("{id:int}/attendance")] public async Task<IActionResult> Attendance(int id, IEnumerable<AttendanceRequest> request) { var r = await service.SetAttendance(id, request); return r.Found ? Ok() : NotFound(); }
    [HttpPut("{id:int}/goalkeepers")] public async Task<IActionResult> Goalkeepers(int id, IEnumerable<int> playerIds) { var r = await service.DesignateGoalkeepers(id, playerIds); return r.Found ? Ok(new { warning = r.Warning }) : NotFound(); }
    [HttpPost("{id:int}/teams")] public async Task<IActionResult> Teams(int id, GenerateRequest request) { var r = await service.Generate(id, request.TeamCount); return r.Error is null ? Ok(new { @event = r.Event, warning = r.Warning }) : BadRequest(r.Error); }
    [HttpPut("{id:int}/teams")] public async Task<IActionResult> Override(int id, IEnumerable<AssignmentRequest> request) => await service.OverrideAssignments(id, request) ? Ok(await service.GetEvent(id)) : BadRequest();
    [HttpPost("{id:int}/teams/swap")] public async Task<IActionResult> Swap(int id, SwapRequest request) => await service.Swap(id, request) ? Ok(await service.GetEvent(id)) : BadRequest();
    [HttpPost("{id:int}/result")] public async Task<IActionResult> Result(int id, ResultRequest request) => await service.RecordResult(id, request) ? Ok(await service.GetEvent(id)) : BadRequest();
    [HttpPost("recompute-ratings")] public async Task<IActionResult> Recompute() { await service.RecomputeRatings(); return Ok(await service.Players()); }
}
