using DocumentProcessorUI.Hubs;
using DocumentProcessorUI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Configure Azure services
builder.Services.Configure<AzureStorageOptions>(
    builder.Configuration.GetSection("AzureStorage"));
builder.Services.Configure<CosmosDbOptions>(
    builder.Configuration.GetSection("CosmosDb"));
builder.Services.Configure<DocumentProcessingOptions>(
    builder.Configuration.GetSection("DocumentProcessing"));

// Register services with local persistence support
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<ICosmosDbService, CosmosDbService>();
builder.Services.AddHttpClient<IFunctionAppService, FunctionAppService>();

// Register the new pipeline service for Bronze-to-Gold automation
builder.Services.AddHttpClient<IDocumentPipelineService, DocumentPipelineService>(client =>
{
    var baseUrl = builder.Configuration.GetValue<string>("AzureFunction:BaseUrl");
    if (!string.IsNullOrEmpty(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl);
    }
});

// Use enhanced service with complete pipeline integration
builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();

// Add session support for progress tracking
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection(); // Only use HTTPS redirection in production
}
else
{
    app.UseDeveloperExceptionPage();
}
app.UseStaticFiles();

app.UseRouting();
app.UseSession();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapHub<DocumentProcessingHub>("/documentProcessingHub");

app.Run();