using AgriStream.Web.Data;
using AgriStream.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgriStream.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;

    public AdminController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Users()
    {
        var users = await _db.Users.Include(u => u.Role).OrderBy(u => u.Username).ToListAsync();
        ViewBag.Message = TempData["Message"];
        return View(users);
    }

    [HttpGet]
    public async Task<IActionResult> Create() =>
        View(new CreateUserViewModel { Roles = await _db.Roles.OrderBy(r => r.RoleName).ToListAsync() });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        model.Roles = await _db.Roles.OrderBy(r => r.RoleName).ToListAsync();
        if (!ModelState.IsValid) return View(model);

        if (await _db.Users.AnyAsync(u => u.Username == model.Username))
        {
            ModelState.AddModelError(nameof(model.Username), "That username is already taken.");
            return View(model);
        }
        if (!model.Roles.Any(r => r.RoleId == model.RoleId))
        {
            ModelState.AddModelError(nameof(model.RoleId), "Choose a valid role.");
            return View(model);
        }

        var user = new AppUser { Username = model.Username.Trim(), RoleId = model.RoleId };
        user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, model.Password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        TempData["Message"] = $"User '{user.Username}' created.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is not null && user.Username != User.Identity?.Name)
        {
            user.IsActive = !user.IsActive;
            await _db.SaveChangesAsync();
            TempData["Message"] = $"User '{user.Username}' is now {(user.IsActive ? "active" : "disabled")}.";
        }
        return RedirectToAction(nameof(Users));
    }
}
