using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AltTextBot.Web.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AltTextBot.Web.Pages.Admin;

[EnableRateLimiting("login")]
public class LoginModel(IOptions<AdminOptions> adminOptions) : PageModel
{
    [BindProperty]
    public string? Password { get; set; }
    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Admin/Index");
        if (HttpContext.Request.Query.ContainsKey("rateLimited"))
            ErrorMessage = "Too many login attempts. Please wait a minute and try again.";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Password ?? ""),
                Encoding.UTF8.GetBytes(adminOptions.Value.Password)))
        {
            ErrorMessage = "Invalid password.";
            return Page();
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, "Admin") };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return RedirectToPage("/Admin/Index");
    }
}
