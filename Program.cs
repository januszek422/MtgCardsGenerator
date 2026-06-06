using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AiMagicCardsGenerator.Data;
using AiMagicCardsGenerator.Repositories;
using AiMagicCardsGenerator.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddScoped<ICardRepository, CardRepository>();
builder.Services.AddScoped<ICardService, CardService>();
builder.Services.AddHttpClient<IScryfallService, ScryfallService>(client => {
    client.BaseAddress = new Uri("https://api.scryfall.com/");
    client.DefaultRequestHeaders.Add("User-Agent", "MtgCardGenerator/1.0");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});
builder.Services.AddHttpClient<IGeneratorService, GeneratorService>();
builder.Services.AddScoped<ICardRenderService, CardRenderService>();
builder.Services.AddScoped<IGeneratedCardRepository, GeneratedCardRepository>();
builder.Services.AddScoped<ICardLikeRepository, CardLikeRepository>();
builder.Services.AddScoped<ICardLikeService, CardLikeService>();
builder.Services.AddSingleton<ILikesBroadcastService, LikesBroadcastService>();
builder.Services.AddHttpClient<IImageGeneratorService, ImageGeneratorService>(client => {
    client.Timeout = TimeSpan.FromSeconds(60); // 60 seconds for image generation
});

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddDefaultUI();
builder.Services.AddControllersWithViews();

builder.Services.AddRateLimiter();

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    
    if(!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));
    
    if(!await roleManager.RoleExistsAsync("User"))
        await roleManager.CreateAsync(new IdentityRole("User"));

    foreach (var user in userManager.Users.ToList()) {
        if(!await userManager.IsInRoleAsync(user, "User"))
            await userManager.AddToRoleAsync(user, "User");
    }

    var adminEmail = string.IsNullOrEmpty(builder.Configuration["AdminEmail"])
        ? null
        : await userManager.FindByEmailAsync(builder.Configuration["AdminEmail"]);
    
    if(adminEmail!=null && !await userManager.IsInRoleAsync(adminEmail, "Admin"))
        await userManager.AddToRoleAsync(adminEmail, "Admin");
}


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.UseMigrationsEndPoint();
}
else {
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
    .WithStaticAssets();

app.Run();