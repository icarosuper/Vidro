namespace VidroApi.Infrastructure.Settings;

public class TrendingSettings
{
    public double ViewCountWeight { get; set; }
    public double LikeCountWeight { get; set; }
    public double TimeDecayFactor { get; set; }
    public int WindowHours { get; set; }
}
