using AgriStream.Web.Data;
using AgriStream.Web.Services;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Application Insights: request, dependency (SQL, Storage, Functions) and performance telemetry (Task f)
builder.Services.AddApplicationInsightsTelemetry();

// Azure SQL Database: user accounts, roles and the device master registry (Task b)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("AgriStreamDb"),
        sql => sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

// Azure Storage: Blob (drone images) and Table (telemetry + audit) clients (Task c)
var storageConnection = builder.Configuration.GetConnectionString("StorageAccount");
builder.Services.AddSingleton(new BlobServiceClient(storageConnection));
builder.Services.AddSingleton(new TableServiceClient(storageConnection));

// Typed HttpClient for the HTTP GET / POST Azure Functions (Task d)
builder.Services.AddHttpClient<FunctionApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Functions:BaseUrl"]!.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Cookie authentication. The auth cookie is self-contained (claims are inside it), so ANY instance
// can validate it - no server-side session state, which keeps the app stateless for scale-out.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Create schema (if the SQL script was not run) and seed roles, users and devices
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await DbSeeder.SeedAsync(db, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialisation failed - check the AgriStreamDb connection string and SQL firewall.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
