using FastGithub.Configuration;
using FastGithub.FlowAnalyze;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Sinks.Network;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Text.Json;

namespace FastGithub
{
    /// <summary>
    /// 启动项
    /// </summary>
    static class Startup
    {
        /// <summary>
        /// 配置通用主机
        /// </summary>
        /// <param name="builder"></param>
        public static void ConfigureHost(this WebApplicationBuilder builder)
        {
            builder.Host.UseSystemd().UseWindowsService();
            builder.Host.UseSerilog((hosting, logger) =>
            {
                var template = "{Timestamp:O} [{Level:u3}]{NewLine}{SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}";
                logger
                    .ReadFrom.Configuration(hosting.Configuration)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(outputTemplate: template)
                    .WriteTo.File(Path.Combine("logs", @"log.txt"), rollingInterval: RollingInterval.Day, outputTemplate: template);

                var udpLoggerPort = hosting.Configuration.GetValue(nameof(AppOptions.UdpLoggerPort), 38457);
                logger.WriteTo.UDPSink(IPAddress.Loopback, udpLoggerPort);
            });
        }

        /// <summary>
        /// 配置web主机
        /// </summary>
        /// <param name="builder"></param>
        public static void ConfigureWebHost(this WebApplicationBuilder builder)
        {
            builder.WebHost.UseShutdownTimeout(TimeSpan.FromSeconds(1d));
            builder.WebHost.UseKestrel(kestrel =>
            {
                kestrel.NoLimit();
                if (OperatingSystem.IsWindows())
                {
                    kestrel.ListenHttpsReverseProxy();
                    kestrel.ListenHttpReverseProxy();
                    kestrel.ListenSshReverseProxy();
                    kestrel.ListenGitReverseProxy();
                }
                else
                {
                    kestrel.ListenHttpProxy();
                }
            });
        }


        /// <summary>
        /// 配置配置
        /// </summary>
        /// <param name="builder"></param>
        public static void ConfigureConfiguration(this WebApplicationBuilder builder)
        {
            const string APPSETTINGS = "appsettings";
            if (Directory.Exists(APPSETTINGS) == true)
            {
                foreach (var file in Directory.GetFiles(APPSETTINGS, "appsettings.*.json"))
                {
                    var jsonFile = Path.Combine(APPSETTINGS, Path.GetFileName(file));
                    builder.Configuration.AddJsonFile(jsonFile, true, true);
                }
            }
        }


        /// <summary>
        /// 配置服务
        /// </summary>
        /// <param name="builder"></param>
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<string, DomainConfig>))]
        public static void ConfigureServices(this WebApplicationBuilder builder)
        {
            var services = builder.Services;
            var configuration = builder.Configuration;

            services.Configure<AppOptions>(configuration);
            services.Configure<FastGithubOptions>(configuration.GetSection(nameof(FastGithub)));

            services.AddConfiguration();
            services.AddDomainResolve();
            services.AddHttpClient();
            services.AddReverseProxy();
            services.AddFlowAnalyze();
            services.AddHostedService<AppHostedService>();

            if (OperatingSystem.IsWindows())
            {
                services.AddPacketIntercept();
            }
        }

        /// <summary>
        /// 配置应用
        /// </summary>
        /// <param name="app"></param>
        public static void ConfigureApp(this WebApplication app)
        {
            app.UseHttpProxyPac();
            app.UseRequestLogging();
            app.UseHttpReverseProxy();

            app.UseRouting();
            app.DisableRequestLogging();

            app.MapGet("/flowStatistics", context =>
            {
                var flowStatistics = context.RequestServices.GetRequiredService<IFlowAnalyzer>().GetFlowStatistics();
                var json = JsonSerializer.Serialize(flowStatistics, FlowStatisticsContext.Default.FlowStatistics);
                return context.Response.WriteAsync(json);
            });
        }
    }
}
