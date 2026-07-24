/* var builder = WebApplication.CreateBuilder(args);

// Services: add authentication / authorization services
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = false
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// =============================================
// EXERCISE 1B: Add custom logging middleware FIRST
// =============================================
app.UseMiddleware<RequestLoggingMiddleware>();

// (Optional) UseExceptionHandler - add it here if you want, 
// but it's fine to leave it out for now as we are just logging.
// app.UseExceptionHandler(); 

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/assessments/results", () => Results.Ok(new
{
    courseCode = "CS-101",
    studentId = "S-001",
    letterGrade = "A"
}))
.RequireAuthorization();

app.Run();
*/




using Microsoft.EntityFrameworkCore;
//using TmsApi.Data;
//using TmsApi.Entities;
using Scalar.AspNetCore;
//using TmsApi.Services;
//using TmsApi.Dtos;
//using TmsApi.Exceptions;
using TmsApi.Api.Exceptions;
using TmsApi.Api.Filters;
using TmsApi.Infrastructure.Persistence;        // for TmsDbContext
using TmsApi.Infrastructure.Services;
using TmsApi.Domain.Entities;                   // if you use entities directly in Program.cs (seed data)
using TmsApi.Application.Interfaces;            // for ICourseService, IEnrollmentService
using TmsApi.Application.DTOs;     
using TmsApi.Api.Options;             // if you use DTOs in minimal APIs
// using TmsApi.Api.Filters;                       // for AuditLogFilter (now in Api project)
//using TmsApi.Data;
using Asp.Versioning;
using TmsApi.Api.Middleware;

using TmsApi.Application.Enrollments.Commands;
using TmsApi.Application.Behaviors;
using FluentValidation;
using MediatR;
using TmsApi.Api.ExceptionHandlers;

var builder = WebApplication.CreateBuilder(args);

// Authentication/Authorization
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = false
        };
    });
builder.Services.AddAuthorization();

// ===== Database Context =====
builder.Services.AddDbContext<TmsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("TmsDatabase"))
        .LogTo(Console.WriteLine, LogLevel.Information)   // prints SQL
        .EnableSensitiveDataLogging());                   // shows parameter values

// ===== Problem Details =====
builder.Services.AddProblemDetails();

/* ===== EXERCISE 5: Add Controllers Service =====
builder.Services.AddControllers();*/
builder.Services.AddControllers(options =>
{
    options.Filters.Add<AuditLogFilter>();
});

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(EnrollStudentHandler).Assembly));

builder.Services.AddValidatorsFromAssembly(typeof(EnrollStudentValidator).Assembly);

// LoggingBehavior FIRST - it must wrap ValidationBehavior
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();


builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});


// ===== OpenAPI =====
builder.Services.AddOpenApi();

// ===== EXERCISE 2: Service Registrations =====
builder.Services.AddSingleton<EnrollmentWorker>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();  // NEW EnrollmentService
builder.Services.AddScoped<ICourseService, CourseService>();

// ===== EXERCISE 3: Options Pattern =====
builder.Services.AddOptions<PaymentOptions>()
    .BindConfiguration("Payments")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Host validation
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddScoped<ICourseRepository, CourseRepository>();
builder.Services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();

var app = builder.Build();
//app.UseExceptionHandler();

// =============================================
// EXERCISE 1B: Add custom logging middleware FIRST
// =============================================
app.UseMiddleware<RequestLoggingMiddleware>();    // from Session 1

// (Optional) UseExceptionHandler - add it here if you want, 
// but it's fine to leave it out for now as we are just logging.
app.UseExceptionHandler();                        // catches exceptions
app.UseStatusCodePages();                         // adds ProblemDetails for status codes like 404

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Environment-aware configuration
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();   // Scalar UI at /scalar/v1
}
// In production, we do NOT map OpenAPI/Scalar, so they return 404.

app.UseMiddleware<V1DeprecationMiddleware>();

// ===== EXERCISE 5: Map Controllers =====
app.MapControllers();

// Session 1 Endpoint
app.MapGet("/api/assessments/results", () => Results.Ok(new
{
    courseCode = "CS-101",
    studentId = "S-001",
    letterGrade = "A"
}))
.RequireAuthorization();

// Session 2 Smoke Test Endpoint
app.MapGet("/api/enrollments/worker-smoke", (EnrollmentWorker worker) =>
{
    worker.ProcessBatch();
    return Results.Ok("processed");
});

/*// ===== TEMPORARY TEST ENDPOINTS - Remove after verifying logging =====
// Changed from MapPost to MapGet for easy browser testing
app.MapGet("/test/enroll", async (IEnrollmentService service, string studentId, string courseCode) =>
{
    var result = await service.EnrollAsync(studentId, courseCode);
    return Results.Ok(result);
});

app.MapGet("/test/enrollment/{id}", async (IEnrollmentService service, string id) =>
{
    var result = await service.GetByIdAsync(id);
    return result is not null ? Results.Ok(result) : Results.NotFound();
});

app.MapDelete("/test/enrollment/{id}", async (IEnrollmentService service, string id) =>
{
    var result = await service.DeleteAsync(id);
    return result ? Results.Ok("Deleted") : Results.NotFound();
});*/

// ===== Test Error Endpoint =====
app.MapGet("/api/error", () =>
{
    throw new TmsDatabaseException("Simulated database failure for ProblemDetails testing");
});



// Seed test data at startup
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
    context.Database.Migrate(); // applies any pending migrations

    if (!context.Students.Any())
    {
        var students = new List<Student>
        {
            new() { RegistrationNumber = "TMS-2026-0001", Name = "Alice Smith", GPA = 3.8m, IsActive = true },
            new() { RegistrationNumber = "TMS-2026-0002", Name = "Bob Jones", GPA = 2.9m, IsActive = true },
            new() { RegistrationNumber = "TMS-2026-0003", Name = "Charlie Brown", GPA = 3.4m, IsActive = false },
            new() { RegistrationNumber = "TMS-2026-0004", Name = "Diana Prince", GPA = 3.9m, IsActive = true },
            new() { RegistrationNumber = "TMS-2026-0005", Name = "Evan Wright", GPA = 2.5m, IsActive = true }
        };
        context.Students.AddRange(students);

        var courses = new List<Course>
        {
            new() { Code = "CS-101", Title = "Introduction to Computer Science", MaxCapacity = 30 },
            new() { Code = "CS-201", Title = "Data Structures and Algorithms", MaxCapacity = 25 },
            new() { Code = "MAT-101", Title = "Calculus I", MaxCapacity = 40 }
        };
        context.Courses.AddRange(courses);

        context.SaveChanges(); // saves Students and Courses

        var enrollments = new List<Enrollment>
        {
            new() { StudentId = students[0].Id, CourseId = courses[0].Id, Grade = 4.0m },
            new() { StudentId = students[0].Id, CourseId = courses[1].Id, Grade = 3.6m },
            new() { StudentId = students[1].Id, CourseId = courses[0].Id, Grade = 2.8m },
            new() { StudentId = students[3].Id, CourseId = courses[1].Id, Grade = 3.9m }
        };
        context.Enrollments.AddRange(enrollments);
        context.SaveChanges();
         
    }
}

// Seed test data at startup (only in Development)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
    DataSeeder.SeedAsync(context).GetAwaiter().GetResult();
}


app.Run();