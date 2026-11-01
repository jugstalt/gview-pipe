using gView.Pipe.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace gView.Pipe.AspNetCore.Extensions.Builder;

static public class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the middleware of <c>AddPipeAuthentication()</c>. Call it early, before <c>UseHttpsRedirection()</c>
    /// and <c>UseAntiforgery()</c>, because it handles the forwarded headers of a reverse proxy.
    /// </summary>
    static public IApplicationBuilder UsePipeAuthentication(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<PipeAuthenticationOptions>();
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("gView.Pipe.Authentication");

        if (options.UseForwardedHeaders)
        {
            app.UseForwardedHeaders();
        }

        switch (options.Mode)
        {
            case PipeAuthenticationMode.None:
                logger.LogWarning("No authentication (Pipe:Auth:Mode = none): everyone who can reach the app may use it.");
                break;
            case PipeAuthenticationMode.Forms:
                if (options.Users.Count == 0)
                {
                    logger.LogError("Pipe:Auth:Mode is forms, but no users are configured: nobody can sign in.");
                }
                foreach (var user in options.Users.Where(u => string.IsNullOrEmpty(u.PasswordHash) && !string.IsNullOrEmpty(u.Password)))
                {
                    logger.LogWarning("The password of user {User} is configured as plain text; prefer a PasswordHash (--hash-password).", user.Name);
                }
                break;
        }

        if (options.Mode != PipeAuthenticationMode.None)
        {
            app.UseAuthentication();
        }
        app.UseAuthorization();
        if (options.Mode == PipeAuthenticationMode.Forms)
        {
            app.UseRateLimiter();
        }

        return app;
    }
}
