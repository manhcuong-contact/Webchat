using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using WEBchat.Models;
using WEBchat.Services;
using BCrypt.Net;

namespace WEBchat.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly MongoService _mongoService;

    public AccountController(MongoService mongoService)
    {
        _mongoService = mongoService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (ModelState.IsValid)
        {
            var user = await _mongoService.Users.Find(u => u.Username == model.Username).FirstOrDefaultAsync();

            if (user != null && BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id!),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim("DisplayName", user.DisplayName),
                    new Claim("Avatar", user.Avatar ?? "/images/default-avatar.png")
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                    });

                if (Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                else
                    return RedirectToAction("Index", "Home");
            }

            ModelState.AddModelError(string.Empty, "Tài khoản hoặc mật khẩu không chính xác.");
        }

        return View(model);
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (ModelState.IsValid)
        {
            var existingUser = await _mongoService.Users.Find(u => u.Username == model.Username).FirstOrDefaultAsync();
            if (existingUser != null)
            {
                ModelState.AddModelError("Username", "Tên đăng nhập này đã được sử dụng.");
                return View(model);
            }

            var newUser = new User
            {
                Username = model.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                DisplayName = model.DisplayName,
                Age = model.Age,
                Email = model.Email,
                Phone = model.Phone,
                Avatar = "/img/default_avatar.png"
            };

            await _mongoService.Users.InsertOneAsync(newUser);
            return RedirectToAction("Login");
        }

        return View(model);
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }
}
