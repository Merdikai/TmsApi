//from module 4 i think we can use this code to create a controller for enrollments.
/*using Microsoft.AspNetCore.Mvc;

namespace TmsApi.Controllers;

[ApiController]
[Route("api/enrollments")]
public class EnrollmentsController : ControllerBase
{
    private readonly IEnrollmentService _enrollmentService;

    public EnrollmentsController(IEnrollmentService enrollmentService)
    {
        _enrollmentService = enrollmentService;
    }

    // GET /api/enrollments - returns all enrollment records
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var enrollments = await _enrollmentService.GetAllAsync();
        return Ok(enrollments);
    }

    // GET /api/enrollments/{id} - returns one record or 404
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var record = await _enrollmentService.GetByIdAsync(id);
        return record is not null ? Ok(record) : NotFound();
    }

    // POST /api/enrollments - creates a new enrollment, returns 201 + Location
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEnrollmentRequest request)
    {
        var record = await _enrollmentService.EnrollAsync(request.StudentId, request.CourseCode);
        return CreatedAtAction(nameof(GetById), new { id = record.Id }, record);
    }

    // DELETE /api/enrollments/{id} - deletes, returns 204 or 404
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _enrollmentService.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}

// Request DTO (can be placed in a separate Models folder, but we keep it here for simplicity)
public record CreateEnrollmentRequest(string StudentId, string CourseCode);*/



using Microsoft.AspNetCore.Mvc;
using TmsApi.Dtos;
using TmsApi.Services;

namespace TmsApi.Controllers;

[ApiController]
[Route("api/courses/{courseId:int}/enrollments")]
public class EnrollmentsController : ControllerBase
{
    private readonly ICourseService _courseService;
    private readonly IEnrollmentService _enrollmentService;

    public EnrollmentsController(ICourseService courseService, IEnrollmentService enrollmentService)
    {
        _courseService = courseService;
        _enrollmentService = enrollmentService;
    }

    [HttpGet("{id:int}", Name = nameof(GetEnrollment))]
    public async Task<IActionResult> GetEnrollment(int courseId, int id, CancellationToken ct)
    {
        var enrollment = await _enrollmentService.GetByIdAsync(courseId, id, ct);
        return enrollment is not null ? Ok(enrollment) : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> EnrollStudent(int courseId, [FromBody] EnrollStudentRequest request, CancellationToken ct)
    {
        // Step 1: Check if course exists
        var course = await _courseService.GetByIdAsync(courseId, ct);
        if (course is null)
        {
            return NotFound();
        }

        // Step 2: Check if course is full
        if (course.EnrollmentCount >= course.MaxCapacity)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Course is full",
                Detail = $"Course '{course.Title}' has reached its maximum capacity of {course.MaxCapacity}.",
                Status = StatusCodes.Status409Conflict,
                Instance = $"/api/courses/{courseId}/enrollments"
            });
        }

        // Step 3: Create enrollment
        var result = await _enrollmentService.CreateAsync(courseId, request, ct);
        return CreatedAtAction(nameof(GetEnrollment), new { courseId, id = result.Id }, result);
    }
}