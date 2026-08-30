using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api;

public static class SettingsRegistration
{
    public static void AddSettings(this IServiceCollection services)
    {
        services.AddOptions<JwtSettings>()
            .BindConfiguration("Jwt")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MinioSettings>()
            .BindConfiguration("MinIO")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ChannelSettings>()
            .BindConfiguration("ChannelSettings")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RateLimitSettings>()
            .BindConfiguration("RateLimit")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<VideoSettings>()
            .BindConfiguration("VideoSettings")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<TrendingSettings>()
            .BindConfiguration("TrendingSettings")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<WebhookSettings>()
            .BindConfiguration("Webhook")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ApiSettings>()
            .BindConfiguration("Api")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ListChannelVideosSettings>()
            .BindConfiguration("ListChannelVideosSettings")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ListFeedVideosSettings>()
            .BindConfiguration("ListFeedVideosSettings")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ListTrendingVideosSettings>()
            .BindConfiguration("ListTrendingVideosSettings")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ListCommentsSettings>()
            .BindConfiguration("ListCommentsSettings")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ListRepliesSettings>()
            .BindConfiguration("ListRepliesSettings")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<StorageCleanupSettings>()
            .BindConfiguration("StorageCleanupSettings")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<JobQueueSettings>()
            .BindConfiguration("JobQueueSettings")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SearchSettings>()
            .BindConfiguration("SearchSettings")
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
