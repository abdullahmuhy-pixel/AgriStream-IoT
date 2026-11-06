using AgriStream.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgriStream.Web.Data;

public static class DbSeeder
{
    // Demo password for the three seeded accounts. Change it after first login in a real system.
    public const string DemoPassword = "AgriStream@2026";

    public static async Task SeedAsync(AppDbContext db, ILogger logger)
    {
        // Creates the tables only if the database is empty (i.e. sql/schema.sql was not run)
        await db.Database.EnsureCreatedAsync();

        foreach (var roleName in new[] { "Admin", "Farmer", "Agronomist" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == roleName))
                db.Roles.Add(new Role { RoleName = roleName });
        }
        await db.SaveChangesAsync();

        if (!await db.Users.AnyAsync())
        {
            var hasher = new PasswordHasher<AppUser>();
            foreach (var (username, roleName) in new[] { ("admin", "Admin"), ("farmer", "Farmer"), ("agronomist", "Agronomist") })
            {
                var role = await db.Roles.FirstAsync(r => r.RoleName == roleName);
                var user = new AppUser { Username = username, RoleId = role.RoleId };
                user.PasswordHash = hasher.HashPassword(user, DemoPassword);
                db.Users.Add(user);
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded demo users admin, farmer and agronomist.");
        }

        if (!await db.Devices.AnyAsync())
        {
            db.Devices.AddRange(
                new Device { DeviceId = "valve-01", FieldId = "field-01", DeviceName = "North Pivot Valve" },
                new Device { DeviceId = "valve-02", FieldId = "field-01", DeviceName = "South Drip Valve" },
                new Device { DeviceId = "valve-03", FieldId = "field-02", DeviceName = "Orchard Drip Valve" },
                new Device { DeviceId = "valve-04", FieldId = "field-03", DeviceName = "Maize Block Pivot" });
            await db.SaveChangesAsync();
        }
    }
}
