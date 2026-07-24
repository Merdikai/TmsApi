using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TmsApi.Application.Enrollments.Commands;
using TmsApi.Application.Enrollments.Queries;

namespace TmsApi.Api.Controllers.V2;

[ApiController]
[Route("api/v{version:apiVersion}/enrollments")]
[ApiVersion("2.0")]
public class EnrollmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public EnrollmentsController(IMediator mediator)
    {
        _mediator = mediator;
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

    [HttpGet("{studentId}/schedule")]
    public async Task<IActionResult> GetSchedule(int studentId, CancellationToken ct)
    {
        var schedule = await _mediator.Send(new GetStudentScheduleQuery(studentId), ct);
        return Ok(schedule);
    }
}