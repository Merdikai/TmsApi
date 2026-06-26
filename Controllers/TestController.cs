using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TmsApi.Data;

namespace TmsApi.Controllers;

[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    private readonly TmsDbContext _context;

    public TestController(TmsDbContext context)
    {
        _context = context;
    }

    // Deferred execution experiment
    [HttpGet("deferred")]
    public IActionResult TestDeferred()
    {
        Console.WriteLine("\n>> STEP 1: Building the query object (no database contact)...");
        var query = _context.Students.Where(s => s.GPA >= 3.0m);

        Console.WriteLine("\n>> STEP 2: Appending a sorting clause...");
        var orderedQuery = query.OrderBy(s => s.Name);

        Console.WriteLine(">> STEP 3: Materializing query into a C# List...");
        var results = orderedQuery.ToList(); // Execution is triggered here

        Console.WriteLine(">> STEP 4: Materialization finished. List populated.\n");
        return Ok(results);
    }

    // Non-translatable helper
    private static bool IsHonorRoll(decimal gpa)
    {
        return gpa >= 3.5m;
    }

    // Translation failure experiment
    [HttpGet("translation-fail")]
    public IActionResult TestTranslationFail()
    {
        Console.WriteLine("\n>> STEP 1: Running non-translatable query...");
        try
        {
            var students = _context.Students
                .Where(s => IsHonorRoll(s.GPA))
                .ToList();
            return Ok(students);
        }
        catch (Exception ex)
        {
            Console.WriteLine($">>> EXCEPTION CAUGHT: {ex.Message}\n");
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpGet("active-high-gpa-count")]
public async Task<IActionResult> ActiveHighGpaCount()
{
    var count = await _context.Students
        .Where(s => s.IsActive && s.GPA >= 3.0m)
        .CountAsync();
    return Ok(new { Count = count });
}

[HttpGet("course-enrollment-counts")]
public async Task<IActionResult> CourseEnrollmentCounts()
{
    var list = await _context.Courses
        .Select(c => new { c.Title, EnrollmentCount = c.Enrollments.Count })
        .OrderByDescending(x => x.EnrollmentCount)
        .ToListAsync();
    return Ok(list);
}

[HttpGet("average-gpa-per-course")]
public async Task<IActionResult> AverageGpaPerCourse()
{
    var list = await _context.Enrollments
        .GroupBy(e => e.Course.Title)
        .Select(g => new { Course = g.Key, AverageGPA = g.Average(e => e.Student.GPA) })
        .ToListAsync();
    return Ok(list);
}

[HttpGet("students-no-enrollments")]
public async Task<IActionResult> StudentsNoEnrollments()
{
    // Approach A: using Any() (translates to NOT EXISTS)
    var usingAny = await _context.Students
        .Where(s => !s.Enrollments.Any())
        .Select(s => s.Name)
        .ToListAsync();

    // Approach B: using LeftJoin (translates to LEFT JOIN ... WHERE ... IS NULL)
    var usingLeftJoin = await _context.Students
        .GroupJoin(_context.Enrollments,
            s => s.Id,
            e => e.StudentId,
            (s, enrollments) => new { Student = s, Enrollments = enrollments })
        .SelectMany(x => x.Enrollments.DefaultIfEmpty(),
            (x, e) => new { x.Student, Enrollment = e })
        .Where(x => x.Enrollment == null)
        .Select(x => x.Student.Name)
        .ToListAsync();

    return Ok(new { UsingAny = usingAny, UsingLeftJoin = usingLeftJoin });
}


}