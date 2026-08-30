using BcryptNet = BCrypt.Net.BCrypt;
using CSharpFunctionalExtensions;
using VidroApi.Application.Common.Logging.Attributes;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Auth;

public static class SignIn
{
    public record Request : IRequest<Result<Response, Error>>
    {
        public string Email { get; init; } = null!;
        [LogIgnore]
        public string Password { get; init; } = null!;
    }

    public record Response
    {
        public string AccessToken { get; init; } = null!;
        [LogIgnore]
        public string RefreshToken { get; init; } = null!;
        public int SecondsToExpiration { get; init; }
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress();

            RuleFor(x => x.Password)
                .NotEmpty();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/auth/signin", async (
            Request req,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(req, ct);
            return result.ToApiResult(StatusCodes.Status200OK);
        }).RequireRateLimiting(RateLimitSettings.AuthPolicy);

    public class Handler(
        AppDbContext db,
        ITokenService tokenService,
        IDateTimeProvider clock,
        IOptions<JwtSettings> jwtOptions)
        : IRequestHandler<Request, Result<Response, Error>>
    {
        public async ValueTask<Result<Response, Error>> Handle(Request req, CancellationToken ct)
        {
            var normalizedEmail = req.Email.ToLowerInvariant();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
            if (user is null)
                return Errors.User.InvalidCredentials();

            var passwordIsValid = BcryptNet.Verify(req.Password, user.PasswordHash);
            if (!passwordIsValid)
                return Errors.User.InvalidCredentials();

            var refreshTokenValue = tokenService.GenerateRefreshToken();
            var refreshTokenExpiresAt = clock.UtcNow.AddDays(jwtOptions.Value.RefreshTokenExpiryDays);
            var refreshToken = new RefreshToken(user.Id, refreshTokenValue, refreshTokenExpiresAt, clock.UtcNow);

            db.RefreshTokens.Add(refreshToken);
            await db.SaveChangesAsync(ct);

            var accessToken = tokenService.GenerateAccessToken(user);
            var secondsToExpiration = jwtOptions.Value.AccessTokenExpiryMinutes * 60;

            return new Response
            {
                AccessToken = accessToken,
                RefreshToken = refreshTokenValue,
                SecondsToExpiration = secondsToExpiration
            };
        }
    }
}
