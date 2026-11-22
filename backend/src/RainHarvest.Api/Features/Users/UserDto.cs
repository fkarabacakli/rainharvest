using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Features.Users;

public sealed record UserDto(Guid Id, string Username, UserRole Role, DateTimeOffset CreatedAt)
{
    public static UserDto From(User u) => new(u.Id, u.Username, u.Role, u.CreatedAt);
}
