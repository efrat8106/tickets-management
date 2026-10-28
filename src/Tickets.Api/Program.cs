using System.Text.Json.Serialization;
using Serilog;
using Tickets.Api.ErrorHandling;
using Tickets.Api.Middleware;
using Tickets.Api.Validation;
using Tickets.Application;
using Tickets.Infrastructure;
using Tickets.Infrastructure.Persistence;
using Tickets.Infrastructure.Seeding;

const string ClientCorsPolicy = "Client";

// "seed [--count N] [--reset]" runs the data seed and exits instead of starting the web server.
SeedOptions? seedOptions;
try
{
    seedOptions = SeedOptions.TryParse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// Seed arguments are not host configuration, so they are not passed to the builder.
var builder = WebApplication.CreateBuilder(seedOptions is null ? args : []);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers(options =>
    {
        options.Filters.Add<FluentValidationFilter>();
        // Required-ness is validated by FluentValidation; avoids a duplicate "The request field is required." error
        // whenever the JSON body itself is malformed.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // Malformed JSON gets a generic message instead of serializer internals (type names, byte positions).
        options.AllowInputFormatterExceptionMessages = false;
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory =
        context => ValidationProblemResults.Create(context.HttpContext, context.ModelState));
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options => options.AddPolicy(ClientCorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

builder.Services.AddHealthChecks().AddDbContextCheck<TicketsDbContext>();

var app = builder.Build();

if (seedOptions is not null)
{
    await app.Services.MigrateDatabaseAsync();
    await app.Services.SeedDatabaseAsync(seedOptions);
    return 0;
}

if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateDatabaseAsync();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(ClientCorsPolicy);

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();
return 0;

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
