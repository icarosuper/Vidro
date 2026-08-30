using System.Security.Claims;
using CSharpFunctionalExtensions;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Channels;

public static class UpdateChannel
{
    public record Request
    {
        public string Name { get; init; } = null!;
        public string? Description { get; init; }
    }

    public record Command : IRequest<UnitResult<Error>>
    {
        public string Handle { get; init; } = null!;
        public Guid UserId { get; init; }
        public string Name { get; init; } = null!;
        public string? Description { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(Channel.NameMaxLength);

            RuleFor(x => x.Description)
                .MaximumLength(Channel.DescriptionMaxLength)
                .When(x => x.Description is not null);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/v1/channels/{handle}", async (
            string handle,
            Request req,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command
            {
                Handle = handle,
                UserId = user.GetUserId(),
                Name = req.Name,
                Description = req.Description
            };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult();
        })
        .RequireAuthorization();

    public class Handler(AppDbContext db, IDateTimeProvider clock)
        : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            var channel = await db.Channels
                .FirstOrDefaultAsync(c => c.Handle == cmd.Handle && c.UserId == cmd.UserId, ct);

            if (channel is null)
                return CommonErrors.NotFound(nameof(Channel), cmd.Handle);

            channel.UpdateDetails(cmd.Name, cmd.Description, clock.UtcNow);
            await db.SaveChangesAsync(ct);

            return UnitResult.Success<Error>();
        }
    }
}
