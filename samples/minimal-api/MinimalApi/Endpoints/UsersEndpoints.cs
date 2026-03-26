using MinimalApi.Models;

namespace MinimalApi.Endpoints;

public static class UsersEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users");

        group.MapGet("/", () => Results.Ok(new[] { new UserDto(1, "Alice", "alice@test.com") }))
            .WithName("GetAllUsers");

        group.MapGet("/{id:int}", (int id) =>
            id > 0 ? Results.Ok(new UserDto(id, "Alice", "alice@test.com")) : Results.NotFound())
            .WithName("GetUserById");

        group.MapPost("/", (CreateUserRequest request) =>
            Results.Created($"/api/users/1", new UserDto(1, request.Name, request.Email)))
            .WithName("CreateUser");
    }
}
