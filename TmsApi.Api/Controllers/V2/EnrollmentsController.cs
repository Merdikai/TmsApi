using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TmsApi.Api.Hubs;
using TmsApi.Application.Hubs;
using TmsApi.Application.Enrollments.Commands;
using TmsApi.Application.Enrollments.Queries;

namespace TmsApi.Api.Controllers.V2;

[ApiController]
[Route("api/v{version:apiVersion}/enrollments")]
[Route("api/enrollments")]
[ApiVersion("2.0")]
[ApiVersion("1.0")]
[Route("api/v1/enrollments")]
public class EnrollmentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IHubContext<TmsApi.Api.Hubs.TmsHub, ITmsHubClient> _hubContext;

    public EnrollmentsController(IMediator mediator, IHubContext<TmsApi.Api.Hubs.TmsHub, ITmsHubClient> hubContext)
    {
        _mediator = mediator;
        _hubContext = hubContext;
    }

    [HttpPost]
    public async Task<IActionResult> Enroll(
        EnrollStudentCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return result.Match<IActionResult>(
            onSuccess: created => Ok(created),
            onFailure: error =>
            {
                var status = error.Code switch
                {
                    "course_not_found" => StatusCodes.Status404NotFound,
                    "course_full" or "already_enrolled" => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest
                };
                return Problem(
                    statusCode: status,
                    title: "Enrollment rejected",
                    detail: error.Message,
                    type: $"https://tms.local/errors/{error.Code}");
            });
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id, CancellationToken ct)
    {
        await _hubContext.Clients.All.ReceiveEnrollmentStatusUpdated(id, "Approved");
        return NoContent();
    }

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(string id, CancellationToken ct)
    {
        await _hubContext.Clients.All.ReceiveEnrollmentStatusUpdated(id, "Rejected");
        return NoContent();
    }

    [HttpGet("{studentId}/schedule")]
    public async Task<IActionResult> GetSchedule(int studentId, CancellationToken ct)
    {
        var schedule = await _mediator.Send(new GetStudentScheduleQuery(studentId), ct);
        return Ok(schedule);
    }
}