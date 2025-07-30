using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Ledger__MVC.Controllers
{
    [AllowAnonymous]
    public class DiagnosticsController : Controller
    {
        [HttpGet("/health")]
        public IActionResult Health()
        {
            return Json(new
            {
                status = "OK",
                timestamp = DateTime.Now,
                server = Environment.MachineName,
                version = "1.0.0"
            });
        }

        [HttpGet("/test")]
        public IActionResult Test()
        {
            return Content("الموقع يعمل بشكل طبيعي - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }
    }
}