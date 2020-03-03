using System;
using System.Linq;

namespace Kubernetes.Bootstrapper.AppOne
{
    public class App
    {
        public App()
        {
            var host = new WebHostBuilder()
   .UseKestrel()
   .UseUrls("http://localhost:8080")
   .UseStartup<TestStartup>()
   .Build();

            host.Run();

        }
    }
}
