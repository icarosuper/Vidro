using CSharpFunctionalExtensions;
using VidroApi.Application.Common.Logging.Attributes;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;
using RefreshTokenEntity = VidroApi.Domain.Entities.RefreshToken;

namespace VidroApi.Api.Features.Auth;

public static class RenewToken
{
    public record Request : IRequest<Result<Response, Error>>
    {
        [LogIgnore]
        public string RefreshToken { get; init; } = null!;
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
            RuleFor(x => x.RefreshToken).NotEmpty();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/auth/renew-token", async (
            Request req,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(req, ct);
            return result.ToApiResult(StatusCodes.Status200OK);
        });

    public class Handler(
        AppDbContext db,
        ITokenService tokenService,
        IDateTimeProvider clock,
        IOptions<JwtSettings> jwtOptions)
        : IRequestHandler<Request, Result<Response, Error>>
    {
        public async ValueTask<Result<Response, Error>> Handle(Request req, CancellationToken ct)
        {
            var currentRefreshToken = await db.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == req.RefreshToken, ct);

            if (currentRefreshToken is null)
                return Errors.RefreshToken.NotFound();

            if (currentRefreshToken.IsRevoked)
                return Errors.RefreshToken.Revoked();

            if (currentRefreshToken.IsExpired(clock.UtcNow))
                return Errors.RefreshToken.Expired();

            currentRefreshToken.Revoke();

            var newRefreshTokenValue = tokenService.GenerateRefreshToken();
            var newRefreshTokenExpiresAt = clock.UtcNow.AddDays(jwtOptions.Value.RefreshTokenExpiryDays);
            var newRefreshToken = new RefreshTokenEntity(currentRefreshToken.UserId, newRefreshTokenValue, newRefreshTokenExpiresAt, clock.UtcNow);

            db.RefreshTokens.Add(newRefreshToken);
            await db.SaveChangesAsync(ct);

            var newAccessToken = tokenService.GenerateAccessToken(currentRefreshToken.User);
            var secondsToExpiration = jwtOptions.Value.AccessTokenExpiryMinutes * 60;

            return new Response
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshTokenValue,
                SecondsToExpiration = secondsToExpiration
            };
        }
    }
}
