using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Disposables;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities;
using Nuke.Docker;
using Nuke.Helm;
using Nuke.Kubernetes;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Logger;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Docker.DockerTasks;
using static Nuke.Helm.HelmTasks;

namespace Kubernetes.Bootstrapper
{
    public abstract class BaseBuild : NukeBuild
    {

        private const string ReleaseConfiguration = "Release";
        private const string DEFAULT_BUILD_ID_NUMBER = "local";

        [GitRepository]
        protected readonly GitRepository GitRepository;

        [Parameter("Target runtime")]
        protected readonly string TargetRuntime = "debian-x64";
        [Parameter("Dotnet SDK Build image")]
        protected readonly string SDKBuildDockerImage = "microsoft/dotnet:2.2-sdk";
        [Parameter("Webservices runtime image")]
        protected readonly string WebservicesRuntimeDockerImage = "microsoft/dotnet:2.2-aspnetcore-runtime";
        [Parameter("Standard apps runtime image")]
        protected readonly string AppsRuntimeDockerImage = "microsoft/dotnet:2.2-runtime";


        [Parameter("Set the build Id.")]
        protected readonly string BuildId = DEFAULT_BUILD_ID_NUMBER;
        [Parameter("Override docker tags (by default build Id is used.")]
        protected readonly string OverrideDockerTags;
        [Parameter("Set the build Number.")]
        protected readonly string BuildNumber = DEFAULT_BUILD_ID_NUMBER;
        [Parameter("Docker registry")]
        protected readonly string DockerRegistryServer;
        [Parameter("Docker registry user name")]
        protected readonly string DockerRegistryUserName;
        [Parameter("Docker registry password")]
        protected readonly string DockerRegistryPassword;
        [Parameter("Override branch (by default the git branch is user.")]
        protected readonly string OverrideBranch;

        virtual protected AbsolutePath SourceDirectory => RootDirectory / "src";
        virtual protected AbsolutePath TestsDirectory => RootDirectory / "tests";
        virtual protected AbsolutePath ArtifactsDirectory => RootDirectory / "_.artifacts";
        virtual protected AbsolutePath TestsOuputDirectory => RootDirectory / "_.testsOutput";
        virtual protected AbsolutePath NukeBuildProjectDirectory => BuildProjectDirectory;
        virtual protected AbsolutePath NugetConfigFile => RootDirectory / "NuGet.Config";
        virtual protected AbsolutePath ConfigsDirectory => RootDirectory / "configs";
        virtual protected AbsolutePath HelmChartsDirectory => BuildAssemblyDirectory / "helm" / "charts";
        virtual protected AbsolutePath KubeResourcesDirectory => BuildAssemblyDirectory / "kubernetes";

        protected bool IsDefaultBuildId => BuildId == DEFAULT_BUILD_ID_NUMBER;
        protected bool IsDefaultBuildNumber => BuildNumber == DEFAULT_BUILD_ID_NUMBER;
        protected string Branch => OverrideBranch ?? GitRepository?.Branch ?? "NO_GIT_REPOS_DETECTED";
        protected string BEEZUP_BUILD_ID => $"{Environment.MachineName}-{Branch}-{DateTime.UtcNow.ToString("yyyy-MM-dd-hh:mm:ss")}";

        virtual protected AbsolutePath OneForAllDockerFile => BuildAssemblyDirectory / "docker" / "build.nuke.app.dockerfile";

        protected virtual void ShowInformations()
        {
            Info($"Host : {Host}");
            Info($"Branch : {Branch}");
            Info($"BuildId : {BuildId}");
            Info($"BuildNumber: {BuildNumber}");
            Info($"RootDirectory : {RootDirectory}");
            Info($"ArtifactsDirectory : {ArtifactsDirectory}");
            Info($"TestsOuputDirectory : {TestsOuputDirectory}");
            Info($"BuildProjectDirectory : {BuildProjectDirectory}");
            Info($"NukeBuildProjectDirectory  : {NukeBuildProjectDirectory}");
            Info($"BuildAssemblyDirectory : {BuildAssemblyDirectory}");
            Info($"DockerRegistryServer : {DockerRegistryServer ?? "-none-"}");
        }


        protected virtual Target Show => _ => _
            .Executes(() =>
            {
                ShowInformations();
            });

        protected virtual Target ListSolutions => _ => _.Executes(() => { ListNames(GetSolutions()); });
        protected virtual Target ListAppsProjects => _ => _.Executes(() => { ListNames(GetApplicationProjects()); });
        protected virtual Target ListTestsProjects => _ => _.Executes(() => { ListNames(GetTestsProjects()); });
        protected virtual Target ListAllProjects => _ => _.Executes(() => { ListNames(GetAllProjects()); });

        private void ListNames(string[] files)
        {
            foreach (var file in files)
            {
                Info(file);
            }
        }

        protected virtual Target CleanArtifacts => _ => _
            .DependsOn(Show)
            .Executes(() =>
            {
                EnsureCleanDirectory(ArtifactsDirectory);
            });

        protected virtual Target Restore => _ => _
            .Executes(() =>
            {
                foreach (var proj in GetAllProjects())
                {
                    Exec(() =>
                    {
                        DotNetRestore(dotNetRestoreSettings => dotNetRestoreSettings
                            .SetProjectFile(proj)
                            .SetConfigFile(NugetConfigFile));
                    });
                }
            });

        protected virtual Target Compile => _ => _
            .Executes(() =>
            {
                foreach (var proj in GetAllProjects())
                {
                    Exec(() =>
                    {
                        DotNetBuild(s => s
                            .SetProjectFile(proj)
                            .SetConfiguration(ReleaseConfiguration)
                            );
                    });
                }
            });

        protected virtual Target CompileSolutions => _ => _
            .Executes(() =>
            {
                foreach (var sln in GetSolutions())
                {
                    Exec(() =>
                    {
                        DotNetBuild(s => s
                            .SetProjectFile(sln)
                            .SetConfiguration(ReleaseConfiguration)
                            );
                    });
                }
            });

        protected virtual Target Test => _ => _
            .Executes(() =>
            {
                ExecuteTests(GetTestsProjects());
            });

        public virtual Target ShowSolutions => _ => _
            .Executes(() =>
            {
                var solutions = GetSolutions().ToArray();

                if (solutions.Length == 0)
                {
                    Error($"No solution found ! in {RootDirectory}");
                    return;
                }

                Info($"Found {solutions.Length} solutions :");

                foreach (var sln in solutions)
                {
                    Info($"- {sln}.");
                }
            });

        public virtual Target PublishAllApplications => _ => _
            .DependsOn(CleanArtifacts)
            .Executes(() =>
            {
                var applications = GetApplicationProjects();

                foreach (var proj in applications)
                {
                    var projectName = Path.GetFileNameWithoutExtension(proj);
                    var outputPath = ArtifactsDirectory / projectName;

                    Info($"Publishing {projectName} to {outputPath}");

                    Exec(() =>
                    {
                        DotNetRestore(s => s
                            .SetProjectFile(proj)
                            .SetConfigFile(NugetConfigFile)
                            );

                        DotNetPublish(s => s
                            .SetConfiguration(ReleaseConfiguration)
                            .EnableNoRestore()
                            .SetProject(proj)
                            .SetOutput(outputPath)
                            );
                    });
                }

                DeleteDirectories(GlobDirectories(ArtifactsDirectory, "**/runtimes/win*"));
                DeleteDirectories(GlobDirectories(ArtifactsDirectory, "**/runtimes/osx"));
            });

        public virtual Target DockerBuildAllAppContainers => _ => _
            .DependsOn(Show)
            .Executes(() =>
            {
                var applications = GetApplicationProjects();
                BuildContainers(applications.ToArray());
            });

        public virtual Target DockerPushAllAppImages => _ => _
            .DependsOn(Show)
            .After(DockerBuildAllAppContainers)
            .Requires(() => DockerRegistryServer, () => DockerRegistryUserName, () => DockerRegistryPassword)
            .Executes(() =>
            {
                var applications = GetApplicationProjects();
                PushContainers(applications.ToArray());
            });


        public virtual Target DockerCleanAllAppImages => _ => _
            .DependsOn(Show)
            .After(DockerBuildAllAppContainers, DockerPushAllAppImages)
            .Executes(() =>
            {
                var images = GetApplicationProjects()
                    .Select(p => $"{GetProjectDockerImageName(p)}:{GetProjectDockerTagName()}")
                    .ToArray();

                DockerRmi(s => s
                    .SetImages(images)
                    .EnableForce()
                    );
            });

        protected virtual Target CompileSolutionsAndTestAll => _ => _
            .DependsOn(Show)
            .DependsOn(CompileSolutions)
            .Executes(() =>
            {
                ExecuteTests(GetTestsProjects(), true);
            });

        protected void ExecuteTests(string[] projects, bool nobuild = false)
        {
            var exceptions = new ConcurrentBag<Exception>();

                Parallel.ForEach(
                    projects,
                    proj =>
                    {
                        try
                        {
                            var projectName = Path.GetFileNameWithoutExtension(proj);
                            DotNetTest(dotNetTestSettings =>
                            {
                                dotNetTestSettings = dotNetTestSettings
                                    .SetConfiguration(ReleaseConfiguration)
                                    .SetResultsDirectory(TestsOuputDirectory)
                                    .SetLogger($"trx;LogFileName={projectName}.trx  ")
                                    .SetProperty("CollectCoverage", true)
                                    .SetProperty("CoverletOutputFormat", "opencover")
                                    .SetProjectFile(proj);

                                if (nobuild)
                                    dotNetTestSettings = dotNetTestSettings.EnableNoBuild();

                                return dotNetTestSettings;
                            });
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    });


            if (exceptions.Count != 0)
                throw new AggregateException(exceptions);

        }


        #region FileSystem

        public static void DeleteDirectories(IEnumerable<string> directories)
        {
            foreach (var dir in directories.Distinct().ToArray())
                try
                {
                    if (!DirectoryExists((AbsolutePath)dir))
                    {
                        Warn($"Not existing directory : {dir}");
                        continue;
                    }

                    DeleteDirectory(dir);
                }
                catch (Exception ex)
                {
                    Warn(ex.Message);
                }
        }

        #endregion


        #region Docker


        protected void PublishApplications(params string[] projects)
        {
            foreach (var proj in projects)
            {
                var projectName = Path.GetFileNameWithoutExtension(proj);
                var outputPath = ArtifactsDirectory / projectName;

                Info($"Publishing {projectName} in {outputPath}");

                Exec(() =>
                {
                    DotNetRestore(s => s
                        .SetProjectFile(proj)
                        .SetConfigFile(NugetConfigFile)
                        );

                    DotNetPublish(s => s
                        .SetConfiguration(ReleaseConfiguration)
                        .EnableNoRestore()
                        .SetProject(proj)
                        .SetOutput(outputPath)
                        );

                    DeleteDirectories(GlobDirectories(outputPath, "**/runtimes/win*"));
                    DeleteDirectories(GlobDirectories(outputPath, "**/runtimes/osx"));
                });
            }
        }

        protected void BuildContainers(params string[] projects)
        {
            var dockerFile = ArtifactsDirectory / Path.GetFileName(OneForAllDockerFile);
            CopyFile(OneForAllDockerFile, dockerFile, FileExistsPolicy.OverwriteIfNewer);

            foreach (var proj in projects)
            {
                var projectName = Path.GetFileNameWithoutExtension(proj);
                var publishedPath = ArtifactsDirectory / projectName;

                Info("Build final container for " + projectName);

                var appConstsFile = GlobFiles(Path.GetDirectoryName(proj), "config.app.consts.yaml").FirstOrDefault();
                if (appConstsFile == null)
                {
                    Warn("Skipped : no 'config.app.consts.yaml' found");
                    continue;
                }

                Exec(() =>
                {
                    DockerBuild(s => s
                        .SetFile(OneForAllDockerFile)
                        .AddBuildArg($"RUNTIME_IMAGE={GetRuntimeImage(proj)}")
                        .AddBuildArg($"PROJECT_NAME={projectName}")
                        .AddBuildArg($"BEEZUP_BUILD_ID={BEEZUP_BUILD_ID}")
                        .SetTag($"{GetProjectDockerImageName(proj)}:{GetProjectDockerTagName()}")
                        .SetPath(publishedPath)
                        .EnableForceRm()
                    );
                });
            }
        }

        protected void PushContainers(params string[] projects) => PushContainers(projects, false);

        protected void PushContainers(string[] projects, bool tagLatest = false)
        {
            DockerLogin(s => s
                .SetServer(DockerRegistryServer)
                .SetUsername(DockerRegistryUserName)
                .SetPassword(DockerRegistryPassword)
            );

            foreach (var proj in projects)
            {
                var imageNameAndTag = $"{GetProjectDockerImageName(proj)}:{GetProjectDockerTagName()}";
                var imageNameAndTagOnRegistry = $"{DockerRegistryServer}/{imageNameAndTag}";

                DockerTag(s => s
                    .SetSourceImage(imageNameAndTag)
                    .SetTargetImage(imageNameAndTagOnRegistry)
                );
                DockerPush(s => s
                    .SetName(imageNameAndTagOnRegistry)
                );

                if (tagLatest)
                {
                    var imageLatestOnRegistry = $"{DockerRegistryServer}/{GetProjectDockerImageName(proj)}";

                    DockerTag(s => s
                        .SetSourceImage(imageNameAndTagOnRegistry)
                        .SetTargetImage(imageLatestOnRegistry)
                    );
                    DockerPush(s => s
                        .SetName(imageLatestOnRegistry)
                    );
                }
            }
        }

        protected void RemoveRepository(string repo)
        {
            throw new NotImplementedException();
        }

        #endregion

        /*********************************************************************************/
        //                      HELM
        /*********************************************************************************/

        protected const string KUBECONFIG = "KUBECONFIG";
        protected const string MONITORING_NAMESPACE_NAME = "default";

        readonly Dictionary<string, string> _kubeEnvironmentVariables = new Dictionary<string, string>();
        protected void ClearKubeEnvironmentVariables() => _kubeEnvironmentVariables.Clear();

        protected void SetKUBECONFIG(string filePath) => _kubeEnvironmentVariables[KUBECONFIG] = filePath;
        protected void ClearKUBECONFIG() => _kubeEnvironmentVariables.Remove(KUBECONFIG);
        protected IDisposable WithKUBECONFIG(string filePath) { SetKUBECONFIG(filePath); return Disposable.Create(ClearKUBECONFIG); }

        protected T KubeEnvVars<T>(T kubeSettings) where T : KubernetesToolSettings => EnvVars(kubeSettings, _kubeEnvironmentVariables);
        protected T HelmEnvVars<T>(T kubeSettings) where T : HelmToolSettings => EnvVars(kubeSettings, _kubeEnvironmentVariables);

        private T EnvVars<T>(T settings, Dictionary<string, string> envVars)
            where T : ToolSettings
        {
            foreach (var v in envVars)
            {
                settings = settings.SetEnvironmentVariable(v.Key, v.Value);
            }
            return settings;
        }

        #region HELM 

        protected void HelmInit()
        {
            HelmTasks.HelmInit(s => HelmEnvVars(s).SetHistoryMax(10));
        }


        protected void HelmUpgradeTiller()
        {
            HelmTasks.HelmInit(s => HelmEnvVars(s).EnableUpgrade());
        }

        /// <summary>
        /// helm upgrade --install --set rbac.create=false,rbac.createRole=false,rbac.createClusterRole=false --namespace kube-system nginx-ingress stable/nginx-ingress
        /// </summary>
        protected void InstallNginxIngress()
        {
            HelmUpgrade(s => HelmEnvVars(s)
                .EnableInstall()
                .AddSet("rbac.create", false).AddSet("rbac.createRole", false).AddSet("rbac.createClusterRole", false)
                .SetNamespace("kube-system")
                .SetRelease("nginx-ingress")
                .AddSet("controller.service.omitClusterIP", "true")
                .AddSet("controller.metrics.enabled", "true").AddSet("controller.metrics.serviceMonitor.enabled", "true") // for prometheus
                .SetChart("stable/nginx-ingress")
                );
        }

   
        /// <summary>
        /// helm upgrade -i --force --set %NSVALUES% %NAMESPACE%-namespace charts/bz-namespace-beezup
        /// </summary>
        protected void InstallNamespace(string group)
        {
            HelmInstall($"{group}-namespace", HelmChartsDirectory / "namespace", group, @namespace: "default");
        }


        /// <summary>
        /// helm upgrade -i --force --set %NSVALUES% -f ../config/%PRODUCT%/%ENV%/environment.yaml --namespace %NAMESPACE% %NAMESPACE%-environment-config charts/imn-environment 
        /// </summary>
        protected void InstallEnvironment(string group, string env, string valuesFile = null)
        {
            valuesFile = valuesFile ?? ConfigsDirectory / env / "environment.yaml";

            HelmInstall($"{group}-environment-config", HelmChartsDirectory / "bz-environment", group, env: env,
                configurator: s =>
                {
                    s = s.AddValues(valuesFile);
                    return s;
                });
        }

        protected string GetDefaultAppChart(AppType appType)
        {
            return $"{appType}".ToLowerInvariant();
        }


        protected void InstallApi(string product, string group, string env, string app, string appName, string valuesFile = null, string chartOverride = null,
            bool recreatePods = false, bool force = false, bool install = false, string imageTag = null, Configure<HelmUpgradeSettings> overrideConfigurator = null)
        {
            InstallApp(AppType.Api, group, env, app, appName, valuesFile, chartOverride, recreatePods, force, install, imageTag, overrideConfigurator);
        }

        /// <summary>
        /// helm upgrade -i --force --recreate-pods --set %NSVALUES%,app=%APP% -f ../config/%PRODUCT%/%ENV%/groups/%GROUP%/%APP%/app.yaml --namespace %NAMESPACE% %NAMESPACE%-%APPNAME% charts/%CHART%
        /// </summary>
        protected void InstallApp(AppType appType, string group, string env, string app, string appName, string valuesFile = null,
            string chartOverride = null, bool recreatePods = false, bool force = false, bool install = false, string imageTag = null, Configure<HelmUpgradeSettings> overrideConfigurator = null)
        {
            var chart = chartOverride ?? GetDefaultAppChart(appType);
            InstallHelmChart(group, env, app, appName, chart, valuesFile, recreatePods, force, install, imageTag, overrideConfigurator);
        }

        protected void InstallHelmChart(string group, string env, string app, string appName, string chart, string valuesFile = null, bool recreatePods = false, bool force = false, bool install = false, string imageTag = null, Configure<HelmUpgradeSettings> overrideConfigurator = null)
        {
            valuesFile = valuesFile ?? ConfigsDirectory / env / "groups" / group / app / "app.yaml";

            HelmInstall($"{group}-{appName}", chart, group, app, env,
                configurator: helmUpgradeSettings =>
                {
                    helmUpgradeSettings = helmUpgradeSettings
                            .AddSet("image.tag", GetDeliveryDockerTagName(imageTag))
                            .AddSet("image.repository", DockerRegistryServer)
                            .AddSet("image.branch", Branch)
                            .AddValues(valuesFile);

                    if (recreatePods)
                        helmUpgradeSettings = helmUpgradeSettings.EnableRecreatePods();
                    if (force)
                        helmUpgradeSettings = helmUpgradeSettings.EnableForce();
                    if (install)
                        helmUpgradeSettings = helmUpgradeSettings.EnableInstall();

                    if (overrideConfigurator != null)
                        helmUpgradeSettings = overrideConfigurator(helmUpgradeSettings);

                    return helmUpgradeSettings;
                });
        }


        private IReadOnlyCollection<Output> HelmInstall(string releaseName, string chart, string group, string app = default, string env = default, string @namespace = default, Configure<HelmUpgradeSettings> configurator = null)
        {
            return HelmUpgrade(helmUpgradeSettings =>
            {
                helmUpgradeSettings = HelmEnvVars(helmUpgradeSettings)
                    .EnableInstall()
                    .SetRelease(releaseName)
                    .SetChart(chart);

                helmUpgradeSettings = helmUpgradeSettings.SetNamespace(@namespace ?? group);

                if (env != default)
                    helmUpgradeSettings = helmUpgradeSettings.AddSet($"{AppConfig.Environment}", env);
                if (group != default)
                    helmUpgradeSettings = helmUpgradeSettings.AddSet($"{AppConfig.Group}", group);
                if (app != default)
                    helmUpgradeSettings = helmUpgradeSettings.AddSet($"{AppConfig.App}", app);

                if (configurator != null)
                    helmUpgradeSettings = configurator.Invoke(helmUpgradeSettings);

                return helmUpgradeSettings;
            });
        }

        #endregion


        protected virtual string[] GetAllProjects()
        {
            return GetApplicationProjects()
                .Concat(GetTestsProjects())
                .Concat(GetNugetPackageProjects())
                .Distinct()
                .OrderBy(s => s)
                .ToArray();
        }

        protected virtual string[] GetSolutions(AbsolutePath directory = null, bool sharded = false)
        {
            directory = directory ?? RootDirectory;

            var result = GlobFiles(directory, "**/*.sln").NotEmpty().OrderBy(p => p);

            return result.ToArray();
        }

        protected virtual string[] GetTestsProjects()
        {
            var result = GlobFiles(TestsDirectory, $"**/*.Tests.csproj").NotEmpty().OrderBy(p => p);
            return result.ToArray();
        }

        protected virtual string[] GetApplicationProjects(AbsolutePath directory = null)
        {
            directory = directory ?? SourceDirectory;

            var result = GlobFiles(directory, "**/*.csproj").NotEmpty()
                .Where(p => IsRunable(p))
                .OrderBy(p => p);


            return result.ToArray();
        }

        protected virtual string[] GetNugetPackageProjects()
        {
            var result = GlobFiles(SourceDirectory, "**/*.csproj").NotEmpty().OrderBy(p => p);
            return result.ToArray();
        }

        protected static bool IsNotExcluded(string project, params string[] excluded)
        {
            var pName = Path.GetFileNameWithoutExtension(project);
            var isExcluded = excluded.Any(e => string.Compare(e, pName, StringComparison.InvariantCultureIgnoreCase) == 0);
            return !isExcluded;
        }

        protected string GetProjectDockerImageName(string project)
        {
            var prefix = GetProjectDockerPrefix(project);
            return $"{prefix}-{Branch.Replace("/", "")}".ToLower();
        }

        protected string GetProjectDockerTagName()
        {
            return OverrideDockerTags?.ToLower() ?? BuildId.ToLower();
        }

        protected string GetDeliveryDockerTagName(string tagName)
        {
            return tagName?.ToLower() ?? OverrideDockerTags?.ToLower() ?? BuildId.ToLower();
        }

        protected static string GetProjectDockerPrefix(string project)
        {
            return GetAppNameFromProject(project).ToLower();
        }

        protected static string GetAppNameFromProject(string project)
        {
            return Path.GetFileNameWithoutExtension(project).TrimEnd(".App");
        }

        protected string GetRuntimeImage(string proj) => IsWebService(proj) ? WebservicesRuntimeDockerImage : AppsRuntimeDockerImage;

        protected virtual bool IsRunable(string proj) => IsWebService(proj) || IsStandardApp(proj);

        protected virtual bool IsStandardApp(string proj) => proj.EndsWith(".App.csproj", StringComparison.OrdinalIgnoreCase);

        protected virtual bool IsWebService(string proj) =>  proj.EndsWith(".RestAPI.csproj", StringComparison.OrdinalIgnoreCase);

        protected virtual void Exec(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Error(ex.Message);
                throw;
            }
        }

        protected virtual void ExecuteTargetInContainer(string targetName, params string[] parameters)
        {
            var containerName = $"{targetName.ToLower()}-{Guid.NewGuid()}";
            try
            {
                DockerRun(s =>
                {
                    s = s
                        .SetImage(SDKBuildDockerImage)
                        .AddVolume($"{RootDirectory}:/build")
                        .SetWorkdir("/build")
                        .SetEntrypoint("./build.sh")
                        .AddArgs(targetName)
                        .SetName(containerName);

                    if (parameters.Length != 0)
                        s = s.AddArgs(parameters);

                    return s;
                });
            }
            finally
            {
                DockerRm(s => s
                    .SetContainers(containerName)
                    .EnableForce()
                    );
            }
        }

    }
}
