using Nuke.Common;
using System;
using System.Linq;
using static Nuke.Common.IO.PathConstruction;

namespace Kubernetes.Bootstrapper.AppOne
{
    public class BuildAppOne
    {

        //AbsolutePath BEEZUP_PROD_KUBECONFIG => RootDirectory / ".kube" / "test";

        //[Parameter("Missing environment")] private readonly string EnvironmentToDeliver;
        //[Parameter("Missing group")] private readonly string GroupToDeliver;
        //[Parameter("Missing app array")] private readonly string[] AppsToDeliver;

        //public BuildAppOne()
        //{

        //}

        //public Target Deliver => _ => _
        //   .Requires(() => DockerRegistryServer)
        //   .OnlyWhenStatic(() => !IsDefaultBuildId)
        //   .Executes(() =>
        //   {
        //       Deploy(EnvironmentToDeliver, GroupToDeliver, AppsToDeliver);
        //   });

        //private void Deploy(string environment, string group, string[] appNames)
        //{
        //    using (WithKUBECONFIG(BEEZUP_PROD_KUBECONFIG))
        //    {
  
        //        var lowerCaseAppGroup = group.ToLower();

        //        var env = "production";

        //        InstallNamespace(lowerCaseAppGroup);
        //        InstallEnvironment(lowerCaseAppGroup, environment);

        //        (string app, string appName, AppType appType, string appShortName)[] apps =

        //            appNames
        //                .Select(appName => ($"{lowerCaseAppGroup}.{appName.ToLower()}.restapi", $"{appName.ToLower()}-restapi", AppType.Api, $"{group}.{appName}.RestAPI"))
        //                .ToArray();

        //        foreach (var app in apps)
        //        {
        //            InstallApp(app.appType, group, env, app.app, app.appName);
        //        }

        //    }

        //}
    }
}
