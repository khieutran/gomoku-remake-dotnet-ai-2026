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
    private sealed record RegisterRequest(string? Email, string? Password);
    private sealed record LoginRequest(string? Email, string? Password);

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
            if (string.IsNullOrWhiteSpace(request.Email) ||
                !new EmailAddressAttribute().IsValid(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return ApiErrors.Create(context, StatusCodes.Status400BadRequest,
                    "validation_error", "Email and password are required.");
            }

            var email = request.Email.Trim();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email };
            IdentityResult result;
            try
            {
                result = await users.CreateAsync(user, request.Password);
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

            await signIn.SignInAsync(user, isPersistent: false);
            return Results.Created("/api/v1/auth/me", new { userId = user.Id, email = user.Email });
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
            if (user is null || !(
                await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true))
                .Succeeded)
            {
                return ApiErrors.Create(context, StatusCodes.Status401Unauthorized,
                    "invalid_credentials", "Invalid email or password.");
            }

            return Results.Ok(new { userId = user.Id, email = user.Email });
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
                : Results.Ok(new { userId = user.Id, email = user.Email });
        });
    }
}
