using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using Microsoft.IdentityModel.Tokens;
using NLog;
using NLog.Web;
using Philips.IBE.Service.WebAgent.Server.Authentication;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.DBUtilities;
using Philips.IBE.Service.WebAgent.Server.Middleware;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Services;
using Philips.IBE.Service.WebAgent.Server.Utilities;
using Philips.IBE.LicenseValidator;
using System.Diagnostics;
using System.Text;

namespace Philips.IBE.Service.WebAgent.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
            logger.Debug("init main");
            try
            {
                var builder = WebApplication.CreateBuilder(args);
                var fileName = Process.GetCurrentProcess().MainModule?.FileName;
                var path = Path.GetDirectoryName(fileName) ?? AppContext.BaseDirectory; 
                Directory.SetCurrentDirectory(path);


                // Add configuration
                builder.Configuration
                    .SetBasePath(path)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
                    .AddEnvironmentVariables();

                // Bind configuration
                builder.Host.UseNLog();
                builder.Services.AddWindowsService();
                var appConfig = new AppConfiguration(builder.Configuration);
                //appConfig.LoadConfiguration(builder.Configuration);
                builder.Configuration.Bind(appConfig);


                var configManager = new ConfigurationValidator(builder.Configuration);

                // Validate JwtOptions
                var jwtOptions = configManager.GetJwtOptions();
                configManager.ValidateJwtOptions(jwtOptions);

                // Validate AuthenticationConfiguration
                var authConfig = configManager.GetAuthenticationConfiguration();
                configManager.ValidateAuthenticationConfiguration(authConfig);

                // Validate CommonConfiguration
                var commonConfig = configManager.GetCommonConfiguration();
                configManager.ValidateCommonConfiguration(commonConfig);


                // Configure Kestrel
                builder.Services.Configure<KestrelServerOptions>(options =>
                {
                    options.ConfigureHttpsDefaults(options =>
                        options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12);
                });

                // Add feature management
                builder.Services.AddFeatureManagement(builder.Configuration.GetSection("CommonConfiguration"));
                builder.Services.AddScoped<IDBUtils, SQLLiteUtils>();

                //Validate License
                var validator = new Philips.IBE.LicenseValidator.LicenseValidator();

                bool isValid = validator.ValidateSigned("Philips.IBE.Agent", commonConfig.License);

                if (!isValid)
                {
                    throw new Exception("Licensing issue: License is invalid or expired.");
                }

                // Add authentication
                builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(opts =>
                {
                    byte[] signingKeyBytes = Encoding.UTF8.GetBytes(appConfig.JwtOptions.SigningKey);
                    opts.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = appConfig.JwtOptions.Issuer,
                        ValidAudience = appConfig.JwtOptions.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
                        //ClockSkew = TimeSpan.Zero // Ensure token expiry is checked in UTC without any clock skew
                    };
                });

                // Add services to the container
                builder.Services.AddSingleton<JWTInvalidator>();
                builder.Services.AddTransient<JWTInvalidatorMiddleware>();
                builder.Services.AddSingleton(appConfig);
                builder.Services.AddControllers();
                builder.Services.AddScoped<DataProtectionUtility>();
                builder.Services.AddScoped<ICertificateService, CertificateService>();
                builder.Services.AddScoped<ICommunicationDataService, CommunicationDataService>();
                builder.Services.AddSingleton<JwtCreator>();
                builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
                builder.Services.AddScoped<IContractService, ContractService>();
                builder.Services.AddScoped<IHeartBeatService, HeartBeatService>();
                builder.Services.AddScoped<INodeService, NodeService>();
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();
                builder.Services.AddCors(options =>
                {
                    options.AddPolicy("AllowAngularOrigins",
                        builder =>
                        {
                            builder.AllowAnyOrigin()
                                   .AllowAnyHeader()
                                   .AllowAnyMethod();
                        });
                });

                var app = builder.Build();

                // Configure the HTTP request pipeline
                if (app.Environment.IsDevelopment())
                {
                    app.UseSwagger();
                    app.UseSwaggerUI(c =>
                    {
                        c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
                        c.RoutePrefix = string.Empty; // Swagger will be served at the app's root
                    });
                }
                app.UseHttpsRedirection();
                app.UseCors(cors =>
                {
                    cors.SetIsOriginAllowed(origin => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader();
                });
                app.UseMiddleware<JWTInvalidatorMiddleware>();
                
                app.UseDefaultFiles();
                app.UseStaticFiles();
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapControllers();
                app.MapFallbackToFile("/index.html");

                logger.Info("Starting application...");
                app.Run();
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Stopped program because of exception");
                throw;
            }
            finally
            {
                LogManager.Shutdown();
            }
        }
    }
}