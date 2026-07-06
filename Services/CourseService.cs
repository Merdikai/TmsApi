using Microsoft.EntityFrameworkCore;
using TmsApi.Data;
using TmsApi.Dtos;
using TmsApi.Entities;

namespace TmsApi.Services;

public class CourseService : ICourseService
{
    private readonly TmsDbContext _context;
    private readonly ILogger<CourseService> _logger;

    public CourseService(TmsDbContext context, ILogger<CourseService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CourseResponseDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        return await _context.Courses
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CourseResponseDto(
                c.Id,
                c.Code,
                c.Title,
                c.MaxCapacity,
                c.Enrollments.Count
            ))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<CourseResponseDto> CreateAsync(CreateCourseRequest request, CancellationToken ct)
    {
        var course = new Course
        {
            Code = request.Code,
            Title = request.Title,
            MaxCapacity = request.MaxCapacity
        };

        _context.Courses.Add(course);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created course {CourseId} ({Code})", course.Id, course.Code);

        return await GetByIdAsync(course.Id, ct) 
            ?? throw new InvalidOperationException("Failed to retrieve created course");
    }

    public async Task<bool> CodeExistsAsync(string code, CancellationToken ct)
    {
        return await _context.Courses.AnyAsync(c => c.Code == code, ct);
    }

    public async Task<PagedResponse<CourseResponseDto>> GetCoursesAsync(PageRequest request, CancellationToken ct)
    {
        // TODO 1: Start with a no-tracking IQueryable<Course>
        IQueryable<Course> query = _context.Courses.AsNoTracking();

        // TODO 2: If request.Search has a value, append a Where clause
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchTerm = $"%{request.Search}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Title, searchTerm) ||
                EF.Functions.ILike(c.Code, searchTerm));
        }

        // TODO 3: Count BEFORE paging
        var totalCount = await query.CountAsync(ct);

        // TODO 4: Apply OrderBy, then Skip/Take, then Select projection
        // OrderBy logic
        IQueryable<Course> sortedQuery = request.OrderBy?.ToLower() switch
        {
            "code" => request.Descending 
                ? query.OrderByDescending(c => c.Code) 
                : query.OrderBy(c => c.Code),
            "maxcapacity" => request.Descending 
                ? query.OrderByDescending(c => c.MaxCapacity) 
                : query.OrderBy(c => c.MaxCapacity),
            _ => request.Descending 
                ? query.OrderByDescending(c => c.Title) 
                : query.OrderBy(c => c.Title)
        };

        // TODO 5: Skip and Take
        var items = await sortedQuery
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new CourseResponseDto(
                c.Id,
                c.Code,
                c.Title,
                c.MaxCapacity,
                c.Enrollments.Count
            ))
            .ToListAsync(ct);

        // TODO 6: Return the paged response
        return new PagedResponse<CourseResponseDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
}