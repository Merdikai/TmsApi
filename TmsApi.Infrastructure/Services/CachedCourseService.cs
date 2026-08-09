using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using TmsApi.Application.DTOs;
using TmsApi.Application.Interfaces;
using TmsApi.Infrastructure.Caching;
using TmsApi.Infrastructure.Persistence;

namespace TmsApi.Infrastructure.Services;

public class CachedCourseService : ICachedCourseService
{
    private readonly HybridCache _cache;
    private readonly ILogger<CachedCourseService> _logger;
    private readonly TmsDbContext _context;

    public CachedCourseService(HybridCache cache, ILogger<CachedCourseService> logger, TmsDbContext context)
    {
        _cache = cache;
        _logger = logger;
        _context = context;
    }

    public async Task<List<CourseDto>> GetAllCoursesAsync(CancellationToken ct)
    {
        return await _context.Courses
            .AsNoTracking()
            .Select(c => new CourseDto(
                c.Id,
                c.Code,
                c.Title,
                c.MaxCapacity,
                c.Enrollments.Count))
            .ToListAsync(ct);
    }

    public async Task InvalidateCourseCacheAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Invalidating cache tag {Tag}", CacheKeys.CoursesTag);
        await _cache.RemoveByTagAsync(CacheKeys.CoursesTag, ct);
    }
}