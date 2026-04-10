using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AltHeroes.Web.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace AltHeroes.Web.Pages.Admin;

public class LoginModel(IOptions<AdminOptions> adminOptions) : PageModel
{
    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Admin/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            Error = "Password is required.";
            return Page();
        }

        var expected = Encoding.UTF8.GetBytes(adminOptions.Value.Password);
        var provided = Encoding.UTF8.GetBytes(password);

        if (!CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            // Constant-time failure delay to blunt brute force
            await Task.Delay(500);
            Error = "Incorrect password.";
            return Page();
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, "admin") };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return RedirectToPage("/Admin/Index");
    }
}
