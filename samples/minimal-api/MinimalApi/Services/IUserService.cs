using MinimalApi.Models;

namespace MinimalApi.Services;

public interface IUserService
{
    Task<UserDto?> GetByIdAsync(int id);
    Task<IReadOnlyList<UserDto>> GetAllAsync();
    Task<UserDto> CreateAsync(CreateUserRequest request);
}
