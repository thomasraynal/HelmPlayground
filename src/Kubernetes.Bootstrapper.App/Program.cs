using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Kubernetes.Bootstrapper.App
{
    public static class Program
    {
        public static void Main(string[] _)
        {
            var webHost = new WebHostBuilder()
                .UseKestrel()
                .UseStartup<Startup>()
                .Build();

            webHost.Run();
        }
    }
}
