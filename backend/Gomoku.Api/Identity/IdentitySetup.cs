using Gomoku.Application.Identity;
using Gomoku.Infrastructure.Identity;
using Gomoku.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography.X509Certificates;

namespace Gomoku.Api.Identity;

public static class IdentitySetup
{
    public static void AddGomokuIdentity(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("Gomoku")
            ?? throw new InvalidOperationException("ConnectionStrings:Gomoku is required.");
        builder.Services.AddDbContext<GomokuDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            options.User.RequireUniqueEmail = true)
            .AddEntityFrameworkStores<GomokuDbContext>()
            .AddDefaultTokenProviders();
        builder.Services.AddAuthorization();

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "gomoku.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "gomoku.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });

        var keyPath = builder.Configuration["GOMOKU_DP_KEYS_DIR"];
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException("GOMOKU_DP_KEYS_DIR is required outside Development.");
            }
            keyPath = Path.Combine(builder.Environment.ContentRootPath, ".keys");
        }
        Directory.CreateDirectory(keyPath);
        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName("Gomoku.Api")
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        var certificatePath = builder.Configuration["GOMOKU_DP_CERT_FILE"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            var passwordPath = builder.Configuration["GOMOKU_DP_CERT_PASSWORD_FILE"];
            if (string.IsNullOrWhiteSpace(passwordPath))
                throw new InvalidOperationException("GOMOKU_DP_CERT_PASSWORD_FILE is required with GOMOKU_DP_CERT_FILE.");
            var password = File.ReadAllText(passwordPath).TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Data Protection certificate password secret is empty.");
            dataProtection.ProtectKeysWithCertificate(
                X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password));
        }

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IGuestSessionService, GuestSessionService>();
        builder.Services.AddScoped<CurrentOwnerService>();
    }
}
