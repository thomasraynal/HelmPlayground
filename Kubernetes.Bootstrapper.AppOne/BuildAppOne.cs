using Nuke.Common;
using System;
using System.Linq;
using static Nuke.Common.IO.PathConstruction;

namespace Kubernetes.Bootstrapper.AppOne
{
    public class BuildAppOne : BaseBuild
    {

        AbsolutePath BEEZUP_PROD_KUBECONFIG => RootDirectory / "k8s";

        [Parameter("Missing group")] private readonly string GroupToDeliver;
        [Parameter("Missing app array")] private readonly string[] AppsToDeliver;

        Target Deliver => _ => _
        .Requires(() => DockerRegistryServer)
        .OnlyWhenStatic(() => !IsDefaultBuildId || OverrideDockerTags != null)
        .Executes(() =>
        {
            Deploy(GroupToDeliver, AppsToDeliver);
        });

        private void Deploy(string appGroup, string[] appNames)
        {
            using (WithKUBECONFIG(BEEZUP_PROD_KUBECONFIG))
            {
                var rollbarToken = "token";
                var rollbarEnv = "production";

                var lowerCaseAppGroup = appGroup.ToLower();

                var env = "production";

                var (product, group) = ("beezup-mkp-adpt", lowerCaseAppGroup);

                InstallNamespace(product, group);
                InstallProduct(product, group);
                InstallEnvironment(product, group, env);

                (string app, string appName, AppType appType, string appShortName)[] apps =

                    appNames
                        .Select(appName => ($"bz.mkp.adpt.{lowerCaseAppGroup}.{appName.ToLower()}.restapi", $"{appName.ToLower()}-restapi", AppType.Api, $"{appGroup}.{appName}.RestAPI"))
                        .ToArray();

                foreach (var app in apps)
                {
                    InstallApp(app.appType, product, group, env, app.app, app.appName);
                    NotifyRollbar(app.appShortName, rollbarEnv, rollbarToken);
                }

            }

        }
    }
}
