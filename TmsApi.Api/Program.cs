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
using TmsApi.Infrastructure.ExternalServices;
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
using Microsoft.Extensions.Caching.Hybrid;
using TmsApi.Infrastructure.Caching;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using TmsApi.Api.RateLimiting;
using System.Threading.Channels;
using TmsApi.Infrastructure.Transcripts;
using TmsApi.Application.Transcripts;
using TmsApi.Application.Hubs;
using TmsApi.Infrastructure.Workers;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

/* ===== EXERCISE 5: Add Controllers Service =====
builder.Services.AddControllers();*/
builder.Services.AddControllers(options =>
{
    options.Filters.Add<AuditLogFilter>();
});

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };
});
// Production-only - leave commented for lab
// builder.Services.AddStackExchangeRedisCache(options =>
// {
//     options.Configuration = builder.Configuration.GetConnectionString("Redis");
//     options.InstanceName = "tms:";
// });
// builder.Services.AddHybridCache();




builder.Services.AddRateLimiter(options =>
{
    // Global limiter - applies to all requests
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var (partitionKey, tier) = ApiKeyResolver.Resolve(httpContext);

        return tier switch
        {
            ApiKeyTier.Paid => RateLimitPartition.GetTokenBucketLimiter(
                partitionKey: $"paid:{partitionKey}",
                factory: _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 200,
                    TokensPerPeriod = 100,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }),
            ApiKeyTier.Free => RateLimitPartition.GetTokenBucketLimiter(
                partitionKey: $"free:{partitionKey}",
                factory: _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 50,
                    TokensPerPeriod = 25,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }),
            _ => RateLimitPartition.GetTokenBucketLimiter(
                partitionKey: $"anon:{partitionKey}",
                factory: _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 10,
                    TokensPerPeriod = 5,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                    QueueLimit = 0,
                    AutoReplenishment = true
                })
        };
    });

    // Concurrency limiter for expensive transcript endpoint
    options.AddConcurrencyLimiter("transcripts", opt =>
    {
        opt.PermitLimit = 5;      // 5 in-flight transcripts maximum
        opt.QueueLimit = 20;      // Queue up to 20 more
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    // Token bucket for search endpoint
    options.AddTokenBucketLimiter("search", opt =>
    {
        opt.TokenLimit = 10;
        opt.TokensPerPeriod = 5;
        opt.ReplenishmentPeriod = TimeSpan.FromSeconds(10);
        opt.QueueLimit = 2;
    });

    // Customize rejection response
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ts)
            ? (int)ts.TotalSeconds
            : 10;

        var problem = new
        {
            type = "https://tms.local/errors/rate-limited",
            title = "Too Many Requests",
            status = 429,
            detail = "Rate limit exceeded. Please try again later.",
            retryAfter = retryAfter
        };

        await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
    };
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

// In the services section:
builder.Services.AddScoped<ICachedCourseService, CachedCourseService>();

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

builder.Services.AddSignalR();
// After builder.Services.AddSignalR() (we'll add this in Exercise 6)
builder.Services.AddSingleton<ITranscriptStatusStore, InMemoryTranscriptStatusStore>();

// Create the bounded channel for transcript requests
builder.Services.AddSingleton(Channel.CreateBounded<TranscriptRequest>(
    new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait
    }));


builder.Services.AddHostedService<TranscriptWorker>();

builder.Services.AddResiliencePipeline("certificate-api", pipeline =>
{
    pipeline
        // Outer: per-request hard timeout - protects against hangs
        .AddTimeout(TimeSpan.FromSeconds(5))
        // Middle: circuit breaker - protects against sustained outage
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 10,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(15),
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TimeoutRejectedException>(),
            OnOpened = args =>
            {
                Console.WriteLine($"Circuit OPENED - stopping requests to certificate service");
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                Console.WriteLine($"Circuit CLOSED - certificate service recovered");
                return ValueTask.CompletedTask;
            }
        })
        // Inner: retry with jitter - only for transient failures
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TimeoutRejectedException>(),
            OnRetry = args =>
            {
                Console.WriteLine(
                    $"Retry #{args.AttemptNumber} after {args.RetryDelay.TotalMilliseconds:F0}ms ({args.Outcome.Exception?.GetType().Name})");
                return ValueTask.CompletedTask;
            }
        });
});


builder.Services.AddHttpClient<ICertificateService, CertificateService>((sp, client) =>
{
    var baseUrl = sp.GetRequiredService<IConfiguration>().GetValue<string>("TmsApi:PublicBaseUrl")
        ?? "http://localhost:5280";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("alive"),
        tags: new[] { "live" })
    .AddNpgSql(
        builder.Configuration.GetConnectionString("TmsDatabase")!,
        tags: new[] { "ready" });

builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.JsonWriterOptions = new() { Indented = false };
});     

const string ServiceName = "tms-api";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: ServiceName,
        serviceVersion: "1.0.0"))
    .WithTracing(t => t
        .AddSource(ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddMeter(ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());


var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
}).DisableRateLimiting();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).DisableRateLimiting();

//app.UseExceptionHandler();

// =============================================
// EXERCISE 1B: Add custom logging middleware FIRST
// =============================================
app.UseMiddleware<RequestLoggingMiddleware>();    // from Session 1

// (Optional) UseExceptionHandler - add it here if you want, 
// but it's fine to leave it out for now as we are just logging.
app.UseExceptionHandler();                        // catches exceptions
app.UseStatusCodePages();                         // adds ProblemDetails for status codes like 404

app.UseCors("DevCors");
app.UseRouting();
app.UseRateLimiter();
app.MapHub<TmsHub>("/hubs/tms");
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

app.MapGet("/", () => Results.Ok(new
{
    message = "TMS API is running",
    hub = "/hubs/tms"
}));

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

// --- Lab-only fake certificate service ---
var attempts = 0;
app.MapPost("/fake/certificates", async () =>
{
    var n = Interlocked.Increment(ref attempts);

    if (n % 7 == 0)
    {
        // Hang - simulates a downstream that never responds
        await Task.Delay(TimeSpan.FromSeconds(20));
        return Results.Ok(new { Status = "issued", Attempt = n });
    }
    if (n % 3 != 0)
    {
        // Transient: 503 Service Unavailable
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    if (n % 11 == 0)
    {
        // Non-transient: 400 - Polly must NOT retry this
        return Results.BadRequest(new { error = "validation_failed" });
    }
    return Results.Ok(new { Status = "issued", Attempt = n });
}).WithTags("lab-fixtures");

app.Run();