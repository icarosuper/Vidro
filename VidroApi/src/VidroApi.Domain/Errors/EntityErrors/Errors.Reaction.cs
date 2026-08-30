namespace VidroApi.Domain.Errors.EntityErrors;

public static partial class Errors
{
    public static class Reaction
    {
        public static Error NotFound() =>
            new("reaction.not_found", "You have not reacted to this video.", ErrorType.NotFound);
    }
}
