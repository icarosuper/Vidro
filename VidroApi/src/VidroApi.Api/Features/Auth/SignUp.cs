using BcryptNet = BCrypt.Net.BCrypt;
using CSharpFunctionalExtensions;
using VidroApi.Application.Common.Logging.Attributes;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Errors;
using VidroApi.Infrastructure.Settings;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Auth;

public static class SignUp
{
    public record Request : IRequest<Result<Response, Error>>
    {
        public string Username { get; init; } = null!;
        public string Email { get; init; } = null!;
        [LogIgnore]
        public string Password { get; init; } = null!;
    }

    public record Response
    {
        public Guid UserId { get; init; }
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Username)
                .NotEmpty()
                .MinimumLength(User.UsernameMinLength)
                .MaximumLength(User.UsernameMaxLength);

            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(User.EmailMaxLength);

            RuleFor(x => x.Password)
                .NotEmpty()
                .MinimumLength(User.PasswordMinLength);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/auth/signup", async (
            Request req,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(req, ct);
            return result.ToApiResult(StatusCodes.Status201Created);
        }).RequireRateLimiting(RateLimitSettings.AuthPolicy);

    public class Handler(AppDbContext db, IDateTimeProvider clock)
        : IRequestHandler<Request, Result<Response, Error>>
    {
        public async ValueTask<Result<Response, Error>> Handle(Request req, CancellationToken ct)
        {
            var normalizedEmail = req.Email.ToLowerInvariant();

            var emailAlreadyRegistered = await db.Users.AnyAsync(u => u.Email == normalizedEmail, ct);
            if (emailAlreadyRegistered)
                return Errors.User.EmailAlreadyInUse();

            var usernameTaken = await db.Users.AnyAsync(u => u.Username == req.Username, ct);
            if (usernameTaken)
                return Errors.User.UsernameAlreadyTaken();

            var passwordHash = BcryptNet.HashPassword(req.Password);
            var user = new User(req.Username, normalizedEmail, passwordHash, clock.UtcNow);

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            return new Response
            {
                UserId = user.Id
            };
        }
    }
}
