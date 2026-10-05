using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VidroApi.Api.BackgroundServices;
using VidroApi.Domain.Enums;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Videos;

public class VideoReconciliationServiceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ReconcileStaleUploads_SavesProcessingBeforePublishingTheJob()
    {
        var videoId = await CreateStaleVideoWithRawObject();

        await RunReconciliationAsync();

        // Published before the save, a failing save would leave a job running for a PendingUpload
        // video — and the API ignores the webhook of a video not in Processing.
        var published = FakeJobQueueService.Published
            .Should().ContainSingle(job => job.VideoId == videoId.ToString()).Subject;
        published.SavedStatus.Should().Be(VideoStatus.Processing);
        (await ReadStatusAsync(videoId)).Should().Be(VideoStatus.Processing);
    }

    [Fact]
    public async Task ReconcileStaleUploads_WhenPublishFails_VideoStaysPendingUploadAndOthersStillProcess()
    {
        var failingVideoId = await CreateStaleVideoWithRawObject();
        var healthyVideoId = await CreateStaleVideoWithRawObject();
        lock (FakeJobQueueService.FailingVideoIds)
            FakeJobQueueService.FailingVideoIds.Add(failingVideoId.ToString());

        await RunReconciliationAsync();

        // Rolled back, so the next tick finds it again. Committed as Processing, it would sit with
        // no job until the processing timeout marks it Failed.
        (await ReadStatusAsync(failingVideoId)).Should().Be(VideoStatus.PendingUpload);
        (await ReadStatusAsync(healthyVideoId)).Should().Be(VideoStatus.Processing);
    }

    [Fact]
    public async Task ReconcileStaleUploads_WithoutRawObject_MarksVideoFailed()
    {
        var videoId = await CreateStaleVideo();

        await RunReconciliationAsync();

        (await ReadStatusAsync(videoId)).Should().Be(VideoStatus.Failed);
    }

    [Fact]
    public async Task ReconcileStuckProcessing_MarksStuckVideoFailed()
    {
        var videoId = await CreateStuckProcessingVideo();

        await RunStuckReconciliationAsync();

        (await ReadStatusAsync(videoId)).Should().Be(VideoStatus.Failed);
    }

    [Fact]
    public async Task ReconcileStuckProcessing_WhenOneSaveFails_OthersStillMarkedFailed()
    {
        var failingVideoId = await CreateStuckProcessingVideo();
        var healthyVideoId = await CreateStuckProcessingVideo();
        var triggerName = $"fail_{failingVideoId:N}";
        await ExecuteSqlAsync($@"
            CREATE FUNCTION {triggerName}() RETURNS trigger AS $$
            BEGIN RAISE EXCEPTION 'simulated save failure'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER {triggerName} BEFORE UPDATE ON videos
            FOR EACH ROW WHEN (OLD.id = '{failingVideoId}') EXECUTE FUNCTION {triggerName}();");
        try
        {
            await RunStuckReconciliationAsync();
        }
        finally
        {
            await ExecuteSqlAsync($@"DROP TRIGGER {triggerName} ON videos; DROP FUNCTION {triggerName}();");
        }

        // One bad save must not abort the sweep, and the failed entity must not leak into the
        // next video's SaveChanges.
        (await ReadStatusAsync(failingVideoId)).Should().Be(VideoStatus.Processing);
        (await ReadStatusAsync(healthyVideoId)).Should().Be(VideoStatus.Failed);
    }

    private async Task<Guid> CreateStuckProcessingVideo()
    {
        var videoId = await CreateStaleVideo();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var longAgo = DateTimeOffset.UtcNow.AddDays(-1);
        await db.Videos.Where(v => v.Id == videoId).ExecuteUpdateAsync(s => s
            .SetProperty(v => v.Status, VideoStatus.Processing)
            .SetProperty(v => v.UpdatedAt, longAgo));
        return videoId;
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task RunStuckReconciliationAsync() =>
        await CreateService().ReconcileStuckProcessingAsync(CancellationToken.None);

    private VideoReconciliationService CreateService() => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new VideoSettings()),
        Options.Create(new ApiSettings { BaseUrl = "http://localhost" }),
        NullLogger<VideoReconciliationService>.Instance);

    private async Task RunReconciliationAsync()
    {
        await CreateService().ReconcileStaleUploadsAsync(CancellationToken.None);
    }

    private async Task<Guid> CreateStaleVideoWithRawObject()
    {
        var videoId = await CreateStaleVideo();
        lock (FakeMinioService.ExistingKeys)
            FakeMinioService.ExistingKeys.Add($"raw/{videoId}");
        return videoId;
    }

    private async Task<Guid> CreateStaleVideo()
    {
        var username = $"usr{Guid.NewGuid():N}"[..15];
        var email = $"user_{Guid.NewGuid():N}@example.com";
        var password = "StrongPass1!";
        await _client.PostAsJsonAsync("/v1/auth/signup", new { username, email, password });
        var signIn = await _client.PostAsJsonAsync("/v1/auth/signin", new { email, password });
        var signInBody = await signIn.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = signInBody.GetProperty("data").GetProperty("accessToken").GetString()!;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await _client.PostAsJsonAsync("/v1/channels", new { handle = "test-channel", name = "My Channel" });
        var created = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/test-channel/videos", new
        {
            title = "Reconciliation Test Video",
            tags = Array.Empty<string>(),
            visibility = 0
        });
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var videoId = Guid.Parse(createdBody.GetProperty("data").GetProperty("videoId").GetString()!);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expiredAt = DateTimeOffset.UtcNow.AddHours(-1);
        await db.Videos.Where(v => v.Id == videoId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.UploadExpiresAt, expiredAt));

        return videoId;
    }

    private async Task<VideoStatus> ReadStatusAsync(Guid videoId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Videos.AsNoTracking().Where(v => v.Id == videoId).Select(v => v.Status).SingleAsync();
    }
}
