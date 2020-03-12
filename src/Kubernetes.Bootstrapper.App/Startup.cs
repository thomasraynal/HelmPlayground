using EventStore.Client.Lite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Yaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Mime;

namespace Kubernetes.Bootstrapper.One.App
{
    public class Startup
    {
        public IConfigurationRoot ConfigurationRoot { get; set; }

        public Startup(IHostingEnvironment env)
        {
            ConfigurationRoot = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddYamlFile("config.app.yaml", true, false)
                .AddYamlFile("config.group.yaml", true, false)
                .Build();
        }

        public void Configure(IApplicationBuilder app)
        {

            app.UseHealthChecks("/health", 1337,
               new HealthCheckOptions
               {
                   ResponseWriter = async (context, report) =>
                   {
                       var result = JsonConvert.SerializeObject(
                           new
                           {
                               status = report.Status.ToString(),
                               errors = report.Entries.Select(e => new { key = e.Key, value = Enum.GetName(typeof(HealthStatus), e.Value.Status) })
                           });
                       context.Response.ContentType = MediaTypeNames.Application.Json;
                       await context.Response.WriteAsync(result);
                   }
               });

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }

        public void ConfigureServices(IServiceCollection services)
        {
            var appConfig = ConfigurationRoot.GetSection(nameof(MyAppConfig)).Get<MyAppConfig>();
            var groupConfig = ConfigurationRoot.GetSection(nameof(MyGroupConfig)).Get<MyGroupConfig>();


            Console.WriteLine(JsonConvert.SerializeObject(appConfig));
            Console.WriteLine(JsonConvert.SerializeObject(groupConfig));

            services.AddLogging();

            services.AddTransient<IEvent<Guid>, DoThingEvent>();

            services.AddEventStore<Guid, EventStoreRepository<Guid>>("tcp://admin:changeit@localhost:1113")
                    .AddEventStoreCache<Guid, Item>();

            services.AddSingleton(appConfig);
            services.AddSingleton(groupConfig);

            services.AddHealthChecks();
            services.AddMvc();
        }

    }
}
