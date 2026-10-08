using GameMatcher.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<GameMatcher.Services.GameMatcherService>();

// Add in-memory EF Core DB
var connectionString = builder.Configuration.GetConnectionString("GameMatcher")
    ?? "Data Source=game-matcher.db";
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(connectionString));


var app = builder.Build();

// Seed DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
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