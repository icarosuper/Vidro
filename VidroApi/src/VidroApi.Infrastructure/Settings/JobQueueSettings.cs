using System.ComponentModel.DataAnnotations;

namespace VidroApi.Infrastructure.Settings;

public class JobQueueSettings
{
    [Required]
    public string QueueName { get; set; } = null!;
}
