using GameMatcher.Data;
using GameMatcher.GameService;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add in-memory EF Core DB
builder.Services.AddDbContext<AppDbContext>(opt => 
    opt.UseInMemoryDatabase("PlayerGameDb"));

builder.Services.AddScoped<IGameService, GameService>();


var app = builder.Build();

// Seed DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    DbInitializer.Seed(db);
}

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();