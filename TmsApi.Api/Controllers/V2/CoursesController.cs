using TmsApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
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

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCourseById(int id, CancellationToken ct = default)
    {
        var allCourses = await _cachedCourseService.GetAllCoursesAsync(ct);
        var course = allCourses.FirstOrDefault(c => c.Id == id);
        if (course is null)
            return NotFound();

        return Ok(course);
    }

    [HttpPost]
    public async Task<IActionResult> CreateCourse(
        [FromBody] CreateCourseFullDto dto,
        [FromServices] TmsApi.Infrastructure.Persistence.TmsDbContext db,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Title))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error",
                detail: "Course code and title are required.",
                type: "https://tms.local/errors/validation-error");
        }

        var exists = await db.Courses.AnyAsync(c => c.Code.ToUpper() == dto.Code.ToUpper(), ct);
        if (exists)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Duplicate Course",
                detail: $"A course with code {dto.Code} already exists.",
                type: "https://tms.local/errors/duplicate-course");
        }

        var course = new Course
        {
            Code = dto.Code.Trim().ToUpper(),
            Title = dto.Title.Trim(),
            MaxCapacity = dto.MaxCapacity > 0 ? dto.MaxCapacity : 30,
            Department = dto.Department,
            Credits = dto.Credits > 0 ? dto.Credits : 3.0m,
            Summary = dto.Summary,
            Description = dto.Description,
            Prerequisites = dto.Prerequisites,
            LearningOutcomesJson = dto.LearningOutcomesJson,
            SyllabusJson = dto.SyllabusJson,
            IndustrySkillsJson = dto.IndustrySkillsJson,
            InstructorId = dto.InstructorId
        };

        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        await _cachedCourseService.InvalidateCourseCacheAsync();

        return CreatedAtAction(nameof(GetCourseById), new { id = course.Id }, course);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCourseFullDto dto, [FromServices] TmsApi.Infrastructure.Persistence.TmsDbContext db, CancellationToken ct = default)
    {
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course == null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Course not found",
                detail: $"Course with ID {id} was not found.",
                type: "https://tms.local/errors/not-found");
        }

        if (!string.IsNullOrWhiteSpace(dto.Title)) course.Title = dto.Title.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Code)) course.Code = dto.Code.Trim().ToUpper();
        if (dto.MaxCapacity > 0) course.MaxCapacity = dto.MaxCapacity;
        if (dto.Department != null) course.Department = dto.Department;
        if (dto.Credits.HasValue && dto.Credits.Value > 0) course.Credits = dto.Credits.Value;
        if (dto.Summary != null) course.Summary = dto.Summary;
        if (dto.Description != null) course.Description = dto.Description;
        if (dto.Prerequisites != null) course.Prerequisites = dto.Prerequisites;
        if (dto.LearningOutcomesJson != null) course.LearningOutcomesJson = dto.LearningOutcomesJson;
        if (dto.SyllabusJson != null) course.SyllabusJson = dto.SyllabusJson;
        if (dto.IndustrySkillsJson != null) course.IndustrySkillsJson = dto.IndustrySkillsJson;
        if (dto.InstructorId != null) course.InstructorId = dto.InstructorId;

        await db.SaveChangesAsync(ct);
        await _cachedCourseService.InvalidateCourseCacheAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, [FromServices] TmsApi.Infrastructure.Persistence.TmsDbContext db, CancellationToken ct = default)
    {
        var course = await db.Courses.Include(c => c.Enrollments).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course == null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Course not found",
                detail: $"Course with ID {id} was not found.",
                type: "https://tms.local/errors/not-found");
        }

        if (course.Enrollments.Any())
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Course deletion failed",
                detail: "Cannot delete course: active student enrollments exist.",
                type: "https://tms.local/errors/active-enrollments-exist");
        }

        db.Courses.Remove(course);
        await db.SaveChangesAsync(ct);
        await _cachedCourseService.InvalidateCourseCacheAsync();

        return NoContent();
    }
}
