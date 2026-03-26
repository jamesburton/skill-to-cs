using MinimalApi.Models;

namespace MinimalApi.Services;

public class UserService : IUserService
{
    private readonly ILogger<UserService> _logger;

    public UserService(ILogger<UserService> logger)
    {
        _logger = logger;
    }

    public Task<UserDto?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<IReadOnlyList<UserDto>> GetAllAsync() => throw new NotImplementedException();
    public Task<UserDto> CreateAsync(CreateUserRequest request) => throw new NotImplementedException();
}
