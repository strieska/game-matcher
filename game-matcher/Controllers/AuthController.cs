using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GameMatcher.Controllers;

public record LoginRequest(string Password);

[ApiController, Route("api/auth")]
public class AuthController(IConfiguration configuration, IAntiforgery antiforgery, ILogger<AuthController> logger) : ControllerBase
{
    [HttpGet("session")]
    public IActionResult Session()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { isOrganizer = User.Identity?.IsAuthenticated == true, csrfToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var password = configuration["Organizer:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogError("Organizer login unavailable: Organizer:Password is not configured.");
            return Problem("Organizer access has not been configured on this server.", statusCode: 503);
        }
        if (request.Password is null || !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(request.Password)), SHA256.HashData(Encoding.UTF8.GetBytes(password))))
            return Problem("Incorrect organizer password.", statusCode: 401);
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Organizer")], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Ok();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }
}
