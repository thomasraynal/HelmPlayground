using EventStore.Client.Lite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Yaml;
using Microsoft.Extensions.DependencyInjection;
using System;

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

            app.UseHealthChecks("/health", 1337);
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
