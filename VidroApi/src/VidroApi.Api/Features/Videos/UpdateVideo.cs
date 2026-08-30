using System.Security.Claims;
using CSharpFunctionalExtensions;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Common;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Enums;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Videos;

public static class UpdateVideo
{
    public record Request
    {
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public List<string> Tags { get; init; } = [];
        public VideoVisibility Visibility { get; init; }
    }

    public record Command : IRequest<Result<Response, Error>>
    {
        public Guid VideoId { get; init; }
        public Guid UserId { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public List<string> Tags { get; init; } = [];
        public VideoVisibility Visibility { get; init; }
    }

    public record Response
    {
        public Guid VideoId { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public List<string> Tags { get; init; } = [];
        public EnumValue Visibility { get; init; } = null!;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IOptions<VideoSettings> videoOptions)
        {
            RuleFor(x => x.Title)
                .NotEmpty()
                .MaximumLength(Video.TitleMaxLength);

            RuleFor(x => x.Description)
                .MaximumLength(Video.DescriptionMaxLength)
                .When(x => x.Description is not null);

            RuleFor(x => x.Tags)
                .Must((cmd, tags) => tags.Count <= videoOptions.Value.MaxTagsPerVideo)
                .WithMessage(cmd => $"A video cannot have more than {videoOptions.Value.MaxTagsPerVideo} tags.");
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/v1/videos/{videoId:guid}", async (
            Guid videoId,
            Request req,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command
            {
                VideoId = videoId,
                UserId = user.GetUserId(),
                Title = req.Title,
                Description = req.Description,
                Tags = req.Tags,
                Visibility = req.Visibility
            };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult(StatusCodes.Status200OK);
        })
        .RequireAuthorization();

    public class Handler(AppDbContext db, IDateTimeProvider clock)
        : IRequestHandler<Command, Result<Response, Error>>
    {
        public async ValueTask<Result<Response, Error>> Handle(Command cmd, CancellationToken ct)
        {
            var video = await FetchVideo(cmd.VideoId, ct);

            if (video is null)
                return CommonErrors.NotFound(nameof(Video), cmd.VideoId);

            var userIsNotOwner = video.Channel.UserId != cmd.UserId;
            if (userIsNotOwner)
            {
                return video.IsPrivate
                    ? CommonErrors.NotFound(nameof(Video), cmd.VideoId)
                    : Errors.Video.NotOwner();
            }

            video.UpdateDetails(cmd.Title, cmd.Description, cmd.Tags, cmd.Visibility, clock.UtcNow);
            await db.SaveChangesAsync(ct);

            return new Response
            {
                VideoId = video.Id,
                Title = video.Title,
                Description = video.Description,
                Tags = video.Tags,
                Visibility = EnumValue.From(video.Visibility)
            };
        }

        private Task<Video?> FetchVideo(Guid videoId, CancellationToken ct)
        {
            return db.Videos
                .Include(v => v.Channel)
                .FirstOrDefaultAsync(v => v.Id == videoId, ct);
        }
    }
}
