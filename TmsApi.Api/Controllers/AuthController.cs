using Asp.Versioning;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TmsApi.Domain.Entities;

namespace TmsApi.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/auth")]
[Route("api/auth")]
[ApiVersion("1.0")]
public class AuthController : ControllerBase
{
    private readonly UserManager<TmsUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public AuthController(
        UserManager<TmsUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    // ===== Register =====
    public record RegisterRequest(
        string Email,
        string Password,
        string FirstName,
        string LastName,
        string? Role);

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        // Prevent account enumeration by returning a generic response
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return Ok(new { message = "Registration request received." });
        }

        var user = new TmsUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description);
            return BadRequest(new { errors });
        }

        var roleToAssign = string.IsNullOrWhiteSpace(request.Role) ? "Student" : request.Role;

        // Ensure requested role exists
        if (!await _roleManager.RoleExistsAsync(roleToAssign))
        {
            await _roleManager.CreateAsync(new IdentityRole(roleToAssign));
        }

        await _userManager.AddToRoleAsync(user, roleToAssign);
        return StatusCode(201, new { message = "User registered successfully." });
    }

    // ===== Login =====
    public record LoginRequest(string Email, string Password);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return Unauthorized(new { detail = "Invalid credentials." });
        }

        // Check if account is locked out
        if (await _userManager.IsLockedOutAsync(user))
        {
            return StatusCode(423, new { detail = "Account locked due to multiple failed login attempts. Try again in 15 minutes." });
        }

        var validPassword = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!validPassword)
        {
            await _userManager.AccessFailedAsync(user);
            return Unauthorized(new { detail = "Invalid credentials." });
        }

        // Reset failed attempt counter on successful login
        await _userManager.ResetAccessFailedCountAsync(user);

        // Update last login time
        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        return Ok(new
        {
            userId = user.Id,
            email = user.Email,
            firstName = user.FirstName,
            lastName = user.LastName,
            roles = await _userManager.GetRolesAsync(user)
        });
    }

    // ===== Check if email exists =====
    [HttpPost("check-email")]
    public async Task<IActionResult> CheckEmail([FromBody] CheckEmailRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        return Ok(new { exists = user != null });
    }

    public record CheckEmailRequest(string Email);
}
