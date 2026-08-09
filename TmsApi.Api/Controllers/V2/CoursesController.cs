using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TmsApi.Application.DTOs;
using TmsApi.Application.Interfaces;
using TmsApi.Application.Utilities;

namespace TmsApi.Api.Controllers.V2;

[ApiController]
[Route("api/v{version:apiVersion}/courses")]
[ApiVersion("2.0")]
public class CoursesController : ControllerBase
{
    private readonly ICachedCourseService _cachedCourseService;

    public CoursesController(ICachedCourseService cachedCourseService)
    {
        _cachedCourseService = cachedCourseService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCourses(
        [FromQuery] string? fields,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var allCourses = await _cachedCourseService.GetAllCoursesAsync(ct);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var totalCount = allCourses.Count;
        var items = allCourses
            .OrderBy(c => c.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // Apply data shaping with error handling
        IEnumerable<Dictionary<string, object?>> shaped;
        try
        {
            shaped = items.ShapeData(fields, CourseDtoFields.Allowed);
        }
        catch (Exception ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid field(s)",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var hasNext = page < totalPages;
        var hasPrevious = page > 1;

        var links = new List<LinkDto>
        {
            new(Url.Action(nameof(GetCourses), new { page, pageSize, fields })!, "self", "GET")
        };
        if (hasNext)
            links.Add(new(Url.Action(nameof(GetCourses), new { page = page + 1, pageSize, fields })!, "next", "GET"));
        if (hasPrevious)
            links.Add(new(Url.Action(nameof(GetCourses), new { page = page - 1, pageSize, fields })!, "prev", "GET"));

        return Ok(new
        {
            Data = shaped,
            Meta = new { totalCount, page, pageSize, totalPages, hasNext, hasPrevious },
            Links = links
        });
    }
}