using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Errors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Users;

public static class GetCurrentUser
{
    public record Command : IRequest<Result<Response, Error>>
    {
        public Guid UserId { get; init; }
    }

    public record Response
    {
        public Guid UserId { get; init; }
        public string Username { get; init; } = null!;
        public string Email { get; init; } = null!;
        public string? AvatarUrl { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/users/me", async (
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command { UserId = user.GetUserId() };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult(StatusCodes.Status200OK);
        })
        .RequireAuthorization();

    public class Handler(
        AppDbContext db,
        IMinioService minio,
        IOptions<MinioSettings> minioOptions)
        : IRequestHandler<Command, Result<Response, Error>>
    {
        private readonly TimeSpan _avatarUrlTtl = TimeSpan.FromHours(minioOptions.Value.ThumbnailUrlTtlHours);

        public async ValueTask<Result<Response, Error>> Handle(Command cmd, CancellationToken ct)
        {
            var currentUser = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);

            if (currentUser is null)
                return CommonErrors.NotFound(nameof(Domain.Entities.User), cmd.UserId);

            var avatarUrl = await GenerateAvatarUrl(currentUser.AvatarPath);

            return new Response
            {
                UserId = currentUser.Id,
                Username = currentUser.Username,
                Email = currentUser.Email,
                AvatarUrl = avatarUrl
            };
        }

        private async Task<string?> GenerateAvatarUrl(string? path)
        {
            if (path is null)
                return null;

            return await minio.GenerateDownloadUrlAsync(path, _avatarUrlTtl);
        }
    }
}
