using MinimalApi.Endpoints;
using MinimalApi.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();
app.MapUserEndpoints();
app.Run();
