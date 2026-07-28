using TmsApi.Application.DTOs;

namespace TmsApi.Application.Interfaces;

public interface ICachedCourseService
{
    //Task<CourseDto> GetCourseAsync(string code, CancellationToken ct);
    //Task<List<CourseDto>> GetAllCoursesAsync(CancellationToken ct);
   Task InvalidateCourseCacheAsync(CancellationToken ct = default);
    
}