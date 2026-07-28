using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using TmsApi.Application.Interfaces;
using TmsApi.Infrastructure.Caching;

namespace TmsApi.Infrastructure.Services;

public class CachedCourseService : ICachedCourseService
{
    private readonly HybridCache _cache;
    private readonly ILogger<CachedCourseService> _logger;

    public CachedCourseService(HybridCache cache, ILogger<CachedCourseService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task InvalidateCourseCacheAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Invalidating cache tag {Tag}", CacheKeys.CoursesTag);
        await _cache.RemoveByTagAsync(CacheKeys.CoursesTag, ct);
    }
}