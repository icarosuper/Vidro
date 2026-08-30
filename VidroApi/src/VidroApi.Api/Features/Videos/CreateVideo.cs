using System.Security.Claims;
using CSharpFunctionalExtensions;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Enums;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Videos;

public static class CreateVideo
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
        public Guid UserId { get; init; }
        public string Username { get; init; } = null!;
        public string ChannelHandle { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public List<string> Tags { get; init; } = [];
        public VideoVisibility Visibility { get; init; }
    }

    public record Response
    {
        public Guid VideoId { get; init; }
        public string UploadUrl { get; init; } = null!;
        public DateTimeOffset UploadExpiresAt { get; init; }
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
        app.MapPost("/v1/users/{username}/channels/{handle}/videos", async (
            string username,
            string handle,
            Request req,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command
            {
                UserId = user.GetUserId(),
                Username = username,
                ChannelHandle = handle,
                Title = req.Title,
                Description = req.Description,
                Tags = req.Tags,
                Visibility = req.Visibility
            };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult(StatusCodes.Status201Created);
        })
        .RequireAuthorization();

    public class Handler(
        AppDbContext db,
        IMinioService minio,
        IDateTimeProvider clock,
        IOptions<MinioSettings> minioOptions)
        : IRequestHandler<Command, Result<Response, Error>>
    {
        public async ValueTask<Result<Response, Error>> Handle(Command cmd, CancellationToken ct)
        {
            var channel = await db.Channels
                .FirstOrDefaultAsync(c => c.Handle == cmd.ChannelHandle && c.User.Username == cmd.Username, ct);
            if (channel is null)
                return CommonErrors.NotFound(nameof(Channel), $"{cmd.Username}/{cmd.ChannelHandle}");

            var isOwner = channel.UserId == cmd.UserId;
            if (!isOwner)
                return Errors.Channel.NotOwner();

            var ttlHours = minioOptions.Value.UploadUrlTtlHours;
            var uploadExpiresAt = clock.UtcNow.AddHours(ttlHours);

            var video = new Video(channel.Id, cmd.Title, cmd.Description, cmd.Tags,
                cmd.Visibility, uploadExpiresAt, clock.UtcNow);

            var objectKey = $"raw/{video.Id}";
            var ttl = TimeSpan.FromHours(ttlHours);
            var (uploadUrl, _) = await minio.GenerateUploadUrlAsync(objectKey, ttl, ct);

            db.Videos.Add(video);
            await db.SaveChangesAsync(ct);

            return new Response
            {
                VideoId = video.Id,
                UploadUrl = uploadUrl,
                UploadExpiresAt = uploadExpiresAt
            };
        }
    }
}
