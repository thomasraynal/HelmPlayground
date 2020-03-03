using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kubernetes.Bootstrapper.App
{
    public static class Program
    {
        public static void Main(string[] _)
        {
            var webHost = new WebHostBuilder()
                .UseKestrel()
                .UseUrls("http://localhost:5000")
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                    logging.AddConsole();
                    logging.AddDebug();
                })
                .UseStartup<Startup>()
                .Build();

            webHost.Run();
        }
    }
}
