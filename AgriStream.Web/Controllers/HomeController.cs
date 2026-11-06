using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;

namespace AgriStream.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        ViewBag.Instance = InstanceId();
        return View();
    }

    /// <summary>
    /// Lightweight health/identity endpoint. Returns which App Service instance served the request,
    /// so the load-test script can prove that traffic was spread across scaled-out instances.
    /// </summary>
    [HttpGet("/api/status")]
    public IActionResult Status() => Json(new
    {
        status = "ok",
        instance = InstanceId(),
        machine = Environment.MachineName,
        utc = DateTime.UtcNow
    });

    /// <summary>
    /// CPU-bound endpoint used ONLY by the load test (Task e) to push CPU above the
    /// auto-scale threshold (Task g). Simulates the processing cost of a dashboard render.
    /// </summary>
    [HttpGet("/api/stress")]
    public IActionResult Stress(int ms = 150)
    {
        ms = Math.Clamp(ms, 10, 1000);
        var sw = Stopwatch.StartNew();
        var buffer = new byte[1024];
        long iterations = 0;
        while (sw.ElapsedMilliseconds < ms)
        {
            buffer = SHA256.HashData(buffer);
            iterations++;
        }
        return Json(new { instance = InstanceId(), ms = sw.ElapsedMilliseconds, iterations });
    }

    public IActionResult Error() => View();

    private static string InstanceId()
    {
        var id = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID");
        return string.IsNullOrEmpty(id) ? Environment.MachineName : id[..Math.Min(8, id.Length)];
    }
}
