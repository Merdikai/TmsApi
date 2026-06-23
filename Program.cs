var builder = WebApplication.CreateBuilder(args);

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