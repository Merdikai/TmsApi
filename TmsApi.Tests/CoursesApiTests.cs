using System.Net;
using System.Net.Http.Json;

namespace TmsApi.Tests;

public class CoursesApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CoursesApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetCourses_ReturnsOkAndPagedJson()
    {
        // Act - pin the V2 URL (see versioning note in lab)
        var response = await _client.GetAsync("/api/v2.0/courses?page=1&pageSize=10");

        // Assert - check HTTP status 200 OK
        response.EnsureSuccessStatusCode();

        // TMS API contract check: PagedResponse with items/data array
        var page = await response.Content.ReadFromJsonAsync<PagedCoursesJson>();
        Assert.True(page?.Items != null || page?.Data != null);
    }

    [Fact]
    public async Task CreateCourse_InvalidCode_ReturnsValidationError()
    {
        // Act - post invalid payload (empty code) to V2 controller
        var response = await _client.PostAsJsonAsync("/api/v2.0/courses", new
        {
            code = "",
            title = "Intro to TMS Security",
            maxCapacity = 30
        });

        // Assert - validation failure returns 400 Bad Request or 422 Unprocessable Entity
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    private sealed class PagedCoursesJson
    {
        public List<CourseRowJson>? Items { get; set; }
        public List<CourseRowJson>? Data { get; set; }
        public int TotalCount { get; set; }
    }

    private sealed class CourseRowJson
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public int MaxCapacity { get; set; }
        public int EnrollmentCount { get; set; }
    }
}