using DMP.Web.Data;
using DMP.Web.Models;
using DMP.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Stripe;
using System.Globalization;

// إيجاد مجلد wwwroot الحقيقي بغض النظر عن مجلد التشغيل (VS أو terminal)
// AppContext.BaseDirectory = bin\Debug\net9.0\ → نصعد حتى نجد wwwroot
static string? FindWebRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, "wwwroot");
        if (Directory.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return null;
}

var webRootPath = FindWebRoot();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args        = args,
    WebRootPath = webRootPath
});

// Stripe
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// DB
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(opt =>
{
    if (string.IsNullOrEmpty(connectionString) ||
        (connectionString.Contains("localdb") && !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)))
    {
        opt.UseInMemoryDatabase("DMP_Dev");
    }
    else
    {
        opt.UseSqlServer(connectionString);
    }
});

// Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(opt =>
{
    opt.Password.RequireDigit           = true;
    opt.Password.RequiredLength         = 6;
    opt.Password.RequireNonAlphanumeric = true;
    opt.Password.RequireUppercase       = true;
    opt.SignIn.RequireConfirmedAccount   = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.LoginPath        = "/Account/Login";
    opt.LogoutPath       = "/Account/Logout";
    opt.AccessDeniedPath = "/Account/AccessDenied";
});

// في بيئة التطوير: تحقق من صلاحية الجلسة كل دقيقتين بدلاً من 30 دقيقة
// يمنع خطأ FK عند إعادة تعيين قاعدة البيانات مع بقاء الكوكي القديم
builder.Services.Configure<SecurityStampValidatorOptions>(opt =>
    opt.ValidationInterval = TimeSpan.FromMinutes(2));

// Localization
builder.Services.AddLocalization(opt => opt.ResourcesPath = "");
builder.Services.Configure<RequestLocalizationOptions>(opt =>
{
    var cultures = new[] { new CultureInfo("ar"), new CultureInfo("en") };
    opt.DefaultRequestCulture   = new RequestCulture("ar");
    opt.SupportedCultures       = cultures;
    opt.SupportedUICultures     = cultures;
    opt.RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new CookieRequestCultureProvider()
    };
});

builder.Services.AddMvc()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(opt =>
    {
        opt.DataAnnotationLocalizerProvider = (type, factory) =>
            factory.Create(typeof(DMP.Web.SharedResource));
    });

builder.Services.AddScoped<IFileService, DMP.Web.Services.FileService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // تطبيق الـ migrations تلقائياً عند كل تشغيل (يعمل من VS أو terminal)
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    await SeedData.InitializeAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

// خدمة الملفات الثابتة مباشرة من المجلد الحقيقي (يتجاوز StaticWebAssets الذي يسبب 404 في VS)
if (webRootPath != null)
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(webRootPath),
        RequestPath  = ""
    });
else
    app.UseStaticFiles();
app.UseRequestLocalization();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
