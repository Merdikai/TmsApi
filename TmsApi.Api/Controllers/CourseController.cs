using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TmsApi.Domain.Entities;
using TmsApi.Infrastructure.Persistence;

namespace TmsApi.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/courses")]
[Route("api/courses")]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
public class CourseController : ControllerBase
{
    private readonly TmsDbContext _context;
    private readonly IAuthorizationService _authorizationService;

    public CourseController(TmsDbContext context, IAuthorizationService authorizationService)
    {
        _context = context;
        _authorizationService = authorizationService;
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Instructor,Admin")]
    public async Task<IActionResult> UpdateCourse(int id, [FromBody] UpdateCourseDto dto)
    {
        var course = await _context.Courses.FindAsync(id);
        if (course == null)
        {
            return NotFound(new { message = $"Course with ID {id} was not found." });
        }

        // Resource-based authorization check
        var authResult = await _authorizationService.AuthorizeAsync(
            User, course, "CanEditCourse");

        if (!authResult.Succeeded)
        {
            return Forbid(); // 403 Forbidden when caller doesn't own the resource
        }

        // Update the course
        course.Title = dto.Title;
        if (!string.IsNullOrWhiteSpace(dto.Code)) course.Code = dto.Code;
        if (dto.MaxCapacity > 0) course.MaxCapacity = dto.MaxCapacity;

        await _context.SaveChangesAsync();

        return NoContent();
    }
}

public record UpdateCourseDto(string Title, string? Code, int MaxCapacity);
