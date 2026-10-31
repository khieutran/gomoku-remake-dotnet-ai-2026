using System.ComponentModel.DataAnnotations;
using Gomoku.Api.Common;
using Gomoku.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gomoku.Api.Identity;

public static class AuthEndpoints
{
    private sealed record RegisterRequest(string? Email, string? Password, string? DisplayName);
    private sealed record LoginRequest(string? Email, string? Password);

    public const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 128;
    private const int MinDisplayNameLength = 2;
    private const int MaxDisplayNameLength = 24;

    private static object AccountBody(ApplicationUser user) =>
        new { userId = user.Id, email = user.Email, displayName = user.DisplayName };

    // Collapses inner whitespace; letters (any script), digits, spaces and . _ - are allowed.
    private static string? NormalizeDisplayName(string value)
    {
        var name = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var allowed = name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '_' or '-');
        return allowed && name.Length is >= MinDisplayNameLength and <= MaxDisplayNameLength ? name : null;
    }

    // Clients that do not send a display name get the email's local part.
    private static string FallbackDisplayName(string email)
    {
        var cleaned = new string(email.Split('@')[0].Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
        if (cleaned.Length > MaxDisplayNameLength) cleaned = cleaned[..MaxDisplayNameLength];
        return NormalizeDisplayName(cleaned) ?? "Người chơi";
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth");

        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { requestToken = tokens.RequestToken });
        });

        group.MapPost("/register", async (HttpContext context, RegisterRequest request,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            // Report every invalid field at once so the form can mark each one.
            var errors = new List<string>();
            var email = request.Email?.Trim() ?? "";
            if (email.Length == 0 || !new EmailAddressAttribute().IsValid(email)) errors.Add("email_invalid");
            var password = request.Password ?? "";
            if (password.Length < MinPasswordLength) errors.Add("password_too_short");
            else if (password.Length > MaxPasswordLength) errors.Add("password_too_long");
            var displayName = request.DisplayName is null ? null : NormalizeDisplayName(request.DisplayName);
            if (request.DisplayName is not null && displayName is null) errors.Add("display_name_invalid");
            if (errors.Count > 0)
            {
                return ApiErrors.Create(context, StatusCodes.Status400BadRequest,
                    "validation_error", "Account details are invalid.", errors.ToArray());
            }

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = email, Email = email,
                DisplayName = displayName ?? FallbackDisplayName(email),
            };
            IdentityResult result;
            try
            {
                result = await users.CreateAsync(user, password);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "UserNameIndex"
            })
            {
                return ApiErrors.Create(context, StatusCodes.Status409Conflict,
                    "account_exists", "Account already exists.");
            }
            if (!result.Succeeded)
            {
                var duplicate = result.Errors.Any(x => x.Code is "DuplicateEmail" or "DuplicateUserName");
                return ApiErrors.Create(context,
                    duplicate ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest,
                    duplicate ? "account_exists" : "validation_error",
                    duplicate ? "Account already exists." : "Account details are invalid.",
                    result.Errors.Select(x => x.Code).ToArray());
            }

            await signIn.SignInAsync(user, isPersistent: true);
            return Results.Created("/api/v1/auth/me", AccountBody(user));
        }).RequireRateLimiting(AnonymousRateLimiting.Register);

        group.MapPost("/login", async (HttpContext context, LoginRequest request,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return ApiErrors.Create(context, StatusCodes.Status400BadRequest,
                    "validation_error", "Email and password are required.");
            }

            var user = await users.FindByEmailAsync(request.Email.Trim());
            var result = user is null ? SignInResult.Failed
                : await signIn.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);
            if (result.IsLockedOut)
            {
                return ApiErrors.Create(context, StatusCodes.Status423Locked,
                    "account_locked", "Too many failed sign-ins. Try again in a few minutes.");
            }
            if (!result.Succeeded)
            {
                return ApiErrors.Create(context, StatusCodes.Status401Unauthorized,
                    "invalid_credentials", "Invalid email or password.");
            }

            return Results.Ok(AccountBody(user!));
        }).RequireRateLimiting(AnonymousRateLimiting.Login);

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        });

        group.MapGet("/me", async (HttpContext context, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(context.User);
            return user is null
                ? ApiErrors.Create(context, StatusCodes.Status401Unauthorized,
                    "authentication_required", "Sign in to access this account.")
                : Results.Ok(AccountBody(user));
        });
    }
}
