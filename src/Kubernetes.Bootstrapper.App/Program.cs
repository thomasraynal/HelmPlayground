using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kubernetes.Bootstrapper.One.App
{
    public static class Program
    {
        public static void Main(string[] _)
        {
            var webHost = new WebHostBuilder()
                .ConfigureLogging((webHostBuilderContext) =>
                {
                    webHostBuilderContext.AddConsole();
                    webHostBuilderContext.AddDebug();
                })
                 .UseKestrel()
                .UseUrls("http://*:5000", "http://*:1337")
                .UseStartup<Startup>()
                .Build();

            webHost.Run();
        }
    }
}
