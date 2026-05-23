using CodexWidget.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<CodexWidgetWebOptions>()
    .Bind(builder.Configuration.GetSection(CodexWidgetWebOptions.SectionName));
builder.Services.AddCodexWidgetWebJson();

var configuredWebOptions = builder.Configuration.GetSection(CodexWidgetWebOptions.SectionName).Get<CodexWidgetWebOptions>();
var resolvedWebOptions = CodexWidgetWebOptionsResolver.Resolve(builder.Configuration, configuredWebOptions);

builder.WebHost.UseUrls(resolvedWebOptions.BindUrls.ToArray());
builder.Services.AddCodexWidgetWebRuntime(resolvedWebOptions);

if (resolvedWebOptions.EnableCors)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy(
            ResolvedCodexWidgetWebOptions.CorsPolicyName,
            policy => policy
                .WithOrigins(resolvedWebOptions.AllowedCorsOrigins.ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod());
    });
}

var app = builder.Build();

if (resolvedWebOptions.EnableCors)
{
    app.UseCors(ResolvedCodexWidgetWebOptions.CorsPolicyName);
}

if (resolvedWebOptions.ServeStaticFiles)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapWebHealthEndpoints();
app.MapStatusApiEndpoints();

app.Run();

public partial class Program;
