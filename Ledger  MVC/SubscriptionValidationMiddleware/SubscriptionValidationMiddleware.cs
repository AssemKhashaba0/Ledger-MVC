using Ledger__MVC.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Ledger__MVC.SubscriptionValidationMiddleware
{
    public class SubscriptionValidationMiddleware
    {
        private readonly RequestDelegate _next;

        public SubscriptionValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    var user = await dbContext.ApplicationUsers
                        .FirstOrDefaultAsync(u => u.Id == userId);

                    if (user != null && (!user.IsActive || user.SubscriptionEndDate < DateTime.Now))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            Message = "انتهى اشتراكك أو تم إيقاف حسابك. الرجاء التجديد أو التواصل مع الإدارة."
                        });
                        return;
                    }
                }
            }

            await _next(context);
        }
    }
}
