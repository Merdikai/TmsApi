using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TmsApi.Application;
using TmsApi.Application.DTOs;
using TmsApi.Application.Interfaces;

namespace TmsApi.Api.Controllers;

[ApiController]
[Route("api/courses")]
[Tags("Courses")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public class CoursesController : ControllerBase
{
    private readonly ICourseService _courseService;
    private readonly LinkGenerator _linkGenerator;

    public CoursesController(ICourseService courseService, LinkGenerator linkGenerator)
    {
        _courseService = courseService;
        _linkGenerator = linkGenerator;
    }

    // GET /api/courses - paginated collection
   [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<CourseResponseDto>), StatusCodes.Status200OK)]
    [EndpointSummary("List courses with pagination")]
    [EndpointDescription("Returns a paginated, optionally filtered list of TMS courses. PageSize is capped at 50.")]
    public async Task<IActionResult> GetCourses([FromQuery] PageRequest request, CancellationToken ct)
    {
        var result = await _courseService.GetCoursesAsync(request, ct);
        return Ok(result);
    }

    // GET /api/courses/{id} - single item
     [HttpGet("{id:int}", Name = nameof(GetCourseById))]
    [ProducesResponseType(typeof(CourseDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EndpointSummary("Get a course by ID")]
    [EndpointDescription("Returns course details with HATEOAS links. Returns 404 if the course does not exist.")]
public async Task<IActionResult> GetCourseById(int id, CancellationToken ct)
{
    var course = await _courseService.GetByIdAsync(id, ct);
    if (course is null)
        return NotFound();

    // Build links
    var links = new List<LinkDto>();

    // Self link
    var selfLink = _linkGenerator.GetPathByName(HttpContext, nameof(GetCourseById), new { id = course.Id });
    if (selfLink is not null)
        links.Add(new LinkDto(selfLink, "self", "GET"));

    // Update link (PUT)
    var updateLink = _linkGenerator.GetPathByName(HttpContext, nameof(UpdateCourse), new { id = course.Id });
    if (updateLink is not null)
        links.Add(new LinkDto(updateLink, "update", "PUT"));

    // Delete link
    var deleteLink = _linkGenerator.GetPathByName(HttpContext, nameof(DeleteCourse), new { id = course.Id });
    if (deleteLink is not null)
        links.Add(new LinkDto(deleteLink, "delete", "DELETE"));

    // Enrollments list link
    var enrollmentsLink = _linkGenerator.GetPathByName(HttpContext, "ListCourseEnrollments", new { courseId = course.Id });
    if (enrollmentsLink is not null)
        links.Add(new LinkDto(enrollmentsLink, "enrollments", "GET"));

    // Enroll link (conditional: only if course not full)
    if (course.EnrollmentCount < course.MaxCapacity)
    {
        var enrollLink = _linkGenerator.GetPathByName(HttpContext, "CreateEnrollment", new { courseId = course.Id });
        if (enrollLink is not null)
            links.Add(new LinkDto(enrollLink, "enroll", "POST"));
    }

    // Create the detail DTO with links
    var detailDto = new CourseDetailDto(
        course.Id,
        course.Code,
        course.Title,
        course.MaxCapacity,
        course.EnrollmentCount,
        links
    );

    return Ok(detailDto);
}

    // POST /api/courses - create new course
   [HttpPost]
    [ProducesResponseType(typeof(CourseResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EndpointSummary("Create a new course")]
    [EndpointDescription("Creates a course with a unique code. Returns 409 if the course code already exists.")]
    public async Task<IActionResult> CreateCourse([FromBody] CreateCourseRequest request, CancellationToken ct)
    {
        // Check for duplicate code
        if (await _courseService.CodeExistsAsync(request.Code, ct))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Course code already exists",
                Detail = $"A course with code '{request.Code}' is already registered.",
                Status = StatusCodes.Status409Conflict,
                Instance = $"/api/courses"
            });
        }

        var result = await _courseService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetCourseById), new { id = result.Id }, result);
    }

   [HttpPut("{id:int}", Name = nameof(UpdateCourse))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [EndpointSummary("Update an existing course")]
    [EndpointDescription("Updates the course with the specified ID. Returns 404 if not found.")]
public IActionResult UpdateCourse(int id) => throw new NotImplementedException();

 [HttpDelete("{id:int}", Name = nameof(DeleteCourse))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EndpointSummary("Delete a course")]
    [EndpointDescription("Deletes the course with the specified ID. Returns 404 if not found.")]
public IActionResult DeleteCourse(int id) => throw new NotImplementedException();


}