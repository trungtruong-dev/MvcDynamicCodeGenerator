using MvcDynamicCodeGenerator.Services;
using Microsoft.Extensions.Logging; // Thêm dòng này

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<CodeGeneratorService>(); // Đảm bảo dòng này ở đây
builder.Services.AddLogging(); // Đảm bảo Logging được thêm nếu chưa có

// Add services to the container.
builder.Services.AddControllersWithViews(); // Đảm bảo dòng này có

var app = builder.Build();
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();


// Thêm định tuyến cụ thể nếu bạn muốn truy cập GeneratorController trực tiếp
app.MapControllerRoute(
    name: "generator",
    pattern: "{controller=Generator}/{action=Index}/{id?}");


app.Run();