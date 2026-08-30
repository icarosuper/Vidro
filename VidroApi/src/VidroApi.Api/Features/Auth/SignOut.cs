using System.Security.Claims;
using CSharpFunctionalExtensions;
using VidroApi.Application.Common.Logging.Attributes;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Extensions;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Auth;

public static class SignOut
{
    public record Request
    {
        [LogIgnore]
        public string RefreshToken { get; init; } = null!;
    }

    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid UserId { get; init; }
        [LogIgnore]
        public string RefreshToken { get; init; } = null!;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.RefreshToken).NotEmpty();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/auth/signout", async (
            Request req,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command
            {
                UserId = user.GetUserId(),
                RefreshToken = req.RefreshToken
            };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult();
        })
        .RequireAuthorization();

    public class Handler(AppDbContext db) : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            var refreshToken = await db.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == cmd.RefreshToken && rt.UserId == cmd.UserId, ct);

            if (refreshToken is null)
                return Errors.RefreshToken.NotFound();

            refreshToken.Revoke();
            await db.SaveChangesAsync(ct);

            return UnitResult.Success<Error>();
        }
    }
}
