using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TmsApi.Domain.Entities;
using TmsApi.Infrastructure.Persistence;

namespace TmsApi.Api.Controllers.V1;

[ApiController]
[Route("api/v{version:apiVersion}/courses")]
[ApiVersion("1.0")]
public class CoursesController : ControllerBase
{
    private readonly TmsDbContext _context;

    public CoursesController(TmsDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetCourses(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? instructorId = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var baseQuery = _context.Courses
            .Include(c => c.Instructor)
            .Include(c => c.Enrollments)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(instructorId))
        {
            baseQuery = baseQuery.Where(c => c.InstructorId == instructorId);
        }

        var totalCount = await baseQuery.CountAsync(ct);

        var items = await baseQuery
            .OrderBy(c => c.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.Code,
                c.Title,
                c.MaxCapacity,
                c.InstructorId,
                c.Department,
                c.Credits,
                c.Summary,
                c.Description,
                c.Prerequisites,
                c.LearningOutcomesJson,
                c.SyllabusJson,
                c.IndustrySkillsJson,
                InstructorName = c.Instructor != null 
                    ? (c.Instructor.FirstName + " " + c.Instructor.LastName).Trim() 
                    : (!string.IsNullOrWhiteSpace(c.InstructorId) ? c.InstructorId : "Dr. Alex Taylor"),
                EnrollmentCount = c.Enrollments.Count
            })
            .ToListAsync(ct);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Ok(new
        {
            items,
            totalCount,
            page,
            pageSize,
            totalPages,
            hasNext = page < totalPages,
            hasPrevious = page > 1
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCourseById(int id, CancellationToken ct = default)
    {
        var course = await _context.Courses
            .Include(c => c.Instructor)
            .Include(c => c.Enrollments)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (course == null)
        {
            return NotFound(new { message = $"Course with ID {id} was not found." });
        }

        return Ok(new
        {
            course.Id,
            course.Code,
            course.Title,
            course.MaxCapacity,
            course.InstructorId,
            course.Department,
            course.Credits,
            course.Summary,
            course.Description,
            course.Prerequisites,
            course.LearningOutcomesJson,
            course.SyllabusJson,
            course.IndustrySkillsJson,
            InstructorName = course.Instructor != null 
                ? (course.Instructor.FirstName + " " + course.Instructor.LastName).Trim() 
                : (!string.IsNullOrWhiteSpace(course.InstructorId) ? course.InstructorId : "Dr. Alex Taylor"),
            EnrollmentCount = course.Enrollments.Count
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateCourse([FromBody] CreateCourseFullDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = "Code and Title are required." });
        }

        var exists = await _context.Courses.AnyAsync(c => c.Code.ToLower() == request.Code.ToLower(), ct);
        if (exists)
        {
            return Conflict(new { message = $"Course code {request.Code} already exists." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? request.InstructorId ?? "admin";

        var course = new Course
        {
            Code = request.Code.ToUpper().Trim(),
            Title = request.Title.Trim(),
            MaxCapacity = request.MaxCapacity > 0 ? request.MaxCapacity : 30,
            Department = request.Department,
            Credits = request.Credits > 0 ? request.Credits : 3.0m,
            Summary = request.Summary,
            Description = request.Description,
            Prerequisites = request.Prerequisites,
            LearningOutcomesJson = request.LearningOutcomesJson,
            SyllabusJson = request.SyllabusJson,
            IndustrySkillsJson = request.IndustrySkillsJson,
            InstructorId = request.InstructorId ?? userId
        };

        _context.Courses.Add(course);
        await _context.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetCourses), new { id = course.Id }, new
        {
            course.Id,
            course.Code,
            course.Title,
            course.MaxCapacity,
            course.InstructorId,
            InstructorName = userId,
            EnrollmentCount = 0
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCourse(int id, [FromBody] UpdateCourseFullDto dto, CancellationToken ct = default)
    {
        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course == null)
        {
            return NotFound(new { message = $"Course with ID {id} was not found." });
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

        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCourse(int id, CancellationToken ct = default)
    {
        var course = await _context.Courses.Include(c => c.Enrollments).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course == null)
        {
            return NotFound(new { message = $"Course with ID {id} was not found." });
        }

        if (course.Enrollments.Any())
        {
            return Conflict(new { message = "Cannot delete course with active student enrollments." });
        }

        _context.Courses.Remove(course);
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }
}
