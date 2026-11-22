using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Services.AddDbContext<Store>(options => options.UseSqlite(builder.Configuration["DATABASE_URL"] ?? "Data Source=gamehub.db"));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => {
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Store>();
    db.Database.EnsureCreated();
    if (!db.Games.Any())
    {
        db.Games.AddRange(new Game { Name="Orbital Drift", Genre="Racing", PriceCents=1499, Description="Race through zero gravity circuits." },
            new Game { Name="Deep Forest", Genre="Adventure", PriceCents=999, Description="Explore a mysterious wilderness." },
            new Game { Name="Pixel Arena", Genre="Action", PriceCents=0, Description="Fast arcade battles." });
        db.SaveChanges();
    }
    var admin = app.Configuration["ADMIN_EMAIL"];
    var password = app.Configuration["ADMIN_PASSWORD"];
    if (!string.IsNullOrWhiteSpace(admin) && password?.Length >= 12 && !db.Users.Any(u => u.Email == admin.ToLowerInvariant()))
    {
        var user = new Account { Email = admin.ToLowerInvariant(), Role="Admin" };
        user.Hash = new PasswordHasher<Account>().HashPassword(user, password);
        db.Users.Add(user);
        db.SaveChanges();
    }
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) => {
    if (context.Request.Method != "GET" && context.Request.Path.StartsWithSegments("/api") && context.Request.Headers["X-GameHub"].ToString() != "1")
    { context.Response.StatusCode = 400; return; }
    await next();
});
app.MapGet("/health", () => Results.Ok(new { status="ok" }));
app.MapGet("/api/games", async (Store db, string? q) => await db.Games.Where(g => q == null || g.Name.Contains(q) || g.Genre.Contains(q)).OrderBy(g => g.Name).ToListAsync());
app.MapPost("/api/register", async (Login input, Store db) => {
    var email = input.Email.Trim().ToLowerInvariant();
    if (!new EmailAddressAttribute().IsValid(email) || input.Password.Length < 10 || input.Password.Length > 128) return Results.BadRequest("Valid email and 10–128 character password required.");
    if (await db.Users.AnyAsync(u => u.Email == email)) return Results.Conflict();
    var user = new Account { Email=email };
    user.Hash = new PasswordHasher<Account>().HashPassword(user,input.Password);
    db.Users.Add(user);
    try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return Results.Conflict(); }
    return Results.Created("/api/me",new { email });
});
app.MapPost("/api/login", async (Login input, Store db, HttpContext ctx) => {
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == input.Email.Trim().ToLowerInvariant());
    if (user == null || new PasswordHasher<Account>().VerifyHashedPassword(user,user.Hash,input.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    var claims = new[] { new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.Name,user.Email),new Claim(ClaimTypes.Role,user.Role) };
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new { user.Email, user.Role });
});
app.MapGet("/api/me", (ClaimsPrincipal user) => new { email=user.Identity!.Name, role=user.FindFirstValue(ClaimTypes.Role) }).RequireAuthorization();
app.MapPost("/api/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
app.MapGet("/api/library", async (ClaimsPrincipal user, Store db) => {
    var id = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return await db.Ownerships.Where(o => o.UserId == id).Select(o => o.Game).ToListAsync();
}).RequireAuthorization();
app.MapPost("/api/library/{gameId:int}", async (int gameId, ClaimsPrincipal user, Store db) => {
    var id = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    if (!await db.Games.AnyAsync(g => g.Id == gameId)) return Results.NotFound();
    if (await db.Ownerships.AnyAsync(o => o.UserId == id && o.GameId == gameId)) return Results.Conflict();
    db.Ownerships.Add(new Ownership { UserId=id, GameId=gameId });
    try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return Results.Conflict(); }
    return Results.NoContent();
}).RequireAuthorization();
app.MapPost("/api/games", async (Game g, Store db) => {
    if (string.IsNullOrWhiteSpace(g.Name) || g.PriceCents < 0 || g.Name.Length > 120) return Results.BadRequest();
    g.Id=0;
    db.Games.Add(g); await db.SaveChangesAsync(); return Results.Created($"/api/games/{g.Id}",g);
}).RequireAuthorization(p=>p.RequireRole("Admin"));
app.Run();

record Login(string Email, string Password);
class Account { public int Id {get;set;} public string Email {get;set;}=""; public string Hash {get;set;}=""; public string Role {get;set;}="User"; }
class Game { public int Id {get;set;} public string Name {get;set;}=""; public string Genre {get;set;}=""; public int PriceCents {get;set;} public string Description {get;set;}=""; }
class Ownership { public int UserId {get;set;} public int GameId {get;set;} public Game? Game {get;set;} }
class Store(DbContextOptions<Store> options):DbContext(options)
{
    public DbSet<Account> Users => Set<Account>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Ownership> Ownerships => Set<Ownership>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>().HasIndex(x=>x.Email).IsUnique();
        b.Entity<Ownership>().HasKey(x=>new{x.UserId,x.GameId});
        b.Entity<Ownership>().HasOne(x=>x.Game).WithMany().HasForeignKey(x=>x.GameId);
    }
}
