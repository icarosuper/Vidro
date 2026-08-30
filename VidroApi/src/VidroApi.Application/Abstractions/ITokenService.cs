using VidroApi.Domain.Entities;

namespace VidroApi.Application.Abstractions;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
}
