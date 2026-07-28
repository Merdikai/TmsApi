using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TmsApi.Application.DTOs;
using TmsApi.Application.Interfaces;


namespace TmsApi.Api.Controllers.V2;

[ApiController]
[Route("api/v{version:apiVersion}/courses")]
[ApiVersion("2.0")]
public class CoursesController : ControllerBase
{
    private readonly ICourseService _courseService;
    private readonly LinkGenerator _linkGenerator;

    public CoursesController(ICourseService courseService, LinkGenerator linkGenerator)
    {
        _courseService = courseService;
        _linkGenerator = linkGenerator;
    }

    [HttpGet]
public async Task<IActionResult> GetCourses(
    [FromQuery] PageRequest request,
    CancellationToken ct)
{
    var result = await _courseService.GetCoursesAsync(request, ct);
    return Ok(result);
}
}