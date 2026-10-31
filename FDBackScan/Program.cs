using FDBackScan.Configuration;
using FDBackScan.Services;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using System.Reflection;



var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AiSettings>(
    builder.Configuration.GetSection(AiSettings.SectionName));


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AI Scanner API",
        Version = "v1",
        Description = "API для сканирования документов, распознавания через ИИ и генерации XLS.",
        Contact = new OpenApiContact
        {
            Name = "Support",
            Email = "support@example.com"
        }
    });

    // Подключение XML-комментариев (описания из /// <summary>)
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);

    // Поддержка загрузки файлов (IFormFile)
    options.MapType<IFormFile>(() => new OpenApiSchema
    {
        Type = JsonSchemaType.String,
        Format = "binary"
    });

    // (Опционально) JWT-авторизация — раскомментируйте, если нужна
    // options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    // {
    //     Name = "Authorization",
    //     Type = SecuritySchemeType.Http,
    //     Scheme = "bearer",
    //     BearerFormat = "JWT",
    //     In = ParameterLocation.Header,
    //     Description = "Введите JWT-токен"
    // });
    // options.AddSecurityRequirement(new OpenApiSecurityRequirement
    // {
    //     {
    //         new OpenApiSecurityScheme
    //         {
    //             Reference = new OpenApiReference
    //             {
    //                 Type = ReferenceType.SecurityScheme,
    //                 Id = "Bearer"
    //             }
    //         },
    //         Array.Empty<string>()
    //     }
    // });
});

// DI для сканера (из предыдущего шага)
builder.Services.AddSingleton<IScanStore, InMemoryScanStore>();
builder.Services.AddScoped<IAiRecognizer, AiRecognizer>();

builder.Services.AddHttpClient();
// Именованные HttpClient для каждого провайдера
builder.Services.AddHttpClient("Ollama", (sp, client) =>
{
    var ai = sp.GetRequiredService<IOptions<AiSettings>>().Value;
    client.BaseAddress = new Uri(ai.Ollama.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(ai.Ollama.TimeoutSeconds);
});

builder.Services.AddHttpClient("OpenAi", (sp, client) =>
{
    var ai = sp.GetRequiredService<IOptions<AiSettings>>().Value;
    client.BaseAddress = new Uri(ai.OpenAi.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(ai.OpenAi.TimeoutSeconds);
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ai.OpenAi.ApiKey);
});

builder.Services.AddHttpClient("AzureDocIntelligence", (sp, client) =>
{
    var ai = sp.GetRequiredService<IOptions<AiSettings>>().Value;
    client.BaseAddress = new Uri(ai.AzureDocIntelligence.Endpoint);
    client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", ai.AzureDocIntelligence.ApiKey);
});
var app = builder.Build();

// Включаем Swagger только в Development (или всегда, если нужно)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "AI Scanner API v1");
        options.RoutePrefix = "swagger"; // UI по адресу /swagger
        options.DocumentTitle = "AI Scanner API";
        options.DisplayRequestDuration(); // показывать время запроса
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();