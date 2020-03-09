using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using System;
using System.IO;
using Nuke.Docker;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Logger;
using static Nuke.Docker.DockerTasks;
using static Nuke.Helm.HelmTasks;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Nuke.Common.IO;
using Nuke.Helm;
using Nuke.Common.Tooling;
using Nuke.Kubernetes;
using System.Collections.Generic;
using System.Reactive.Disposables;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
public class Build : NukeBuild
{

    [GitRepository]
    private readonly GitRepository GitRepository;

    private readonly Configuration Configuration = Configuration.Release;

    [Parameter("Docker registry")]
    public string DockerRegistryServer;
    [Parameter("Docker registry user name")]
    public string DockerRegistryUserName;
    [Parameter("Docker registry password")]
    public string DockerRegistryPassword;
    [Parameter("Webservices runtime image")]
    public string WebservicesRuntimeDockerImage = "microsoft/dotnet:2.2-aspnetcore-runtime";
    [Parameter("Standard apps runtime image")]
    public string AppsRuntimeDockerImage = "microsoft/dotnet:2.2-runtime";
    [Parameter("Set the build Id.")]
    public string BuildId;
    //[Parameter("Set the group to be deployed")]
    //public string GroupToBeDeploy;
    //[Parameter("Set the apps to be deployed")]
    //public string[] AppsToBeDeployed;

    private AbsolutePath BuildDirectory => RootDirectory / "build";
    private AbsolutePath SourceDirectory => RootDirectory / "src";
    private AbsolutePath TestsDirectory => RootDirectory / "tests";
    private AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    
    //todo : config in the csproj folder
    private AbsolutePath ConfigsDirectory => RootDirectory / "configs";
    private AbsolutePath HelmChartsDirectory => BuildDirectory / "helm" / "charts";
    //private AbsolutePath KubeResourcesDirectory => BuildAssemblyDirectory / "kubernetes";
    private string Branch => GitRepository?.Branch ?? "NO_GIT_REPOS_DETECTED";
    private AbsolutePath OneForAllDockerFile => BuildDirectory / "docker" / "build.nuke.app.dockerfile";

    public static int Main() => Execute<Build>(build => build.Publish);

     Target Test => _ => _
            .DependsOn(Publish)
            .Executes(() =>
            {
                ExecuteTests(GetTestsProjects());
            });

    Target Clean => _ => _
        .Executes(() =>
        {
            //foreach (var dir in directories.Distinct().ToArray())
            //    try
            //    {
            //        if (!DirectoryExists((AbsolutePath)dir))
            //        {
            //            Warn($"Not existing directory : {dir}");
            //            continue;
            //        }

            //        DeleteDirectory(dir);
            //    }
            //    catch (Exception ex)
            //    {
            //        Warn(ex.Message);
            //    }

            //SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            //TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);

            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
            {
                foreach (var proj in GetAllProjects())
                {
                    DotNetRestore(dotNetRestoreSettings => dotNetRestoreSettings
                        .SetProjectFile(proj));

                }
            });

    Target Publish => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {

            ShowInformations();

            var applications = GetAllProjects();

            PublishApplications(applications);

        });

    Target Package => _ => _
        .DependsOn(Test)
        .Executes(() =>
        {
            var applications = GetApplicationProjects();

            BuildContainers(applications);

            PushContainers(applications);
        });


    public Target Deploy => _ => _
        .DependsOn(Package)
        .Executes(() =>
        {
            DeployApps("one","app1");
        });


        //Target CleanPackage => _ => _
        //.After(Package)
        //.Executes(() =>
        //{
        //    var images = GetApplicationProjects()
        //        .Select(p => $"{GetProjectDockerImageName(p)}:{BuildId.ToLower()}")
        //        .ToArray();

        //    DockerRmi(s => s
        //        .SetImages(images)
        //        .EnableForce()
        //        );
        //});


    private void DeployApps(string group, params string[] appNames)
    {

        var lowerCaseAppGroup = group.ToLower();

        HelmRepoUpdate(s => HelmEnvVars(s));
        InstallNamespace(lowerCaseAppGroup);

        InstallGroup(group, BuildDirectory / "config" / "config.group.yaml");

        (string app, string appName, string appShortName)[] apps =
            appNames
                .Select(appName => ($"{lowerCaseAppGroup}.{appName.ToLower()}", $"{appName.ToLower()}", $"{group}.{appName}"))
                .ToArray();

        foreach (var app in apps)
        {
            HelmInstall(app.appName, HelmChartsDirectory / "api", group, $"{group}-namespace");
        }

    }

    private void PushContainers(string[] projects)
    {
        Console.WriteLine(DockerRegistryUserName);
        Console.WriteLine(DockerRegistryPassword);

        DockerLogin(dockerLoginSettings => dockerLoginSettings
            .SetServer(DockerRegistryServer)
            .SetUsername(DockerRegistryUserName)
            .SetPassword(DockerRegistryPassword)
        );

        foreach (var proj in projects)
        {
            var imageNameAndTag = $"{GetProjectDockerImageName(proj)}:{BuildId.ToLower()}";
            var imageNameAndTagOnRegistry = $"{DockerRegistryServer}/{DockerRegistryUserName}/{imageNameAndTag}";

            DockerTag(s => s
                .SetSourceImage(imageNameAndTag)
                .SetTargetImage(imageNameAndTagOnRegistry)
            );
            DockerPush(s => s
                .SetName(imageNameAndTagOnRegistry)
            );

        }
    }

    protected string GetProjectDockerImageName(string project)
    {
       var prefix = Path.GetFileNameWithoutExtension(project).ToLower();

        return $"{prefix}-{GitRepository.Branch.Replace("/", "")}".ToLower();
    }

    protected void BuildContainers(params string[] projects)
    {
        var dockerFile = ArtifactsDirectory / Path.GetFileName(OneForAllDockerFile);
        CopyFile(OneForAllDockerFile, dockerFile, FileExistsPolicy.OverwriteIfNewer);

        foreach (var proj in projects)
        {
            var projectName = Path.GetFileNameWithoutExtension(proj);
            var publishedPath = ArtifactsDirectory / projectName;

            DockerBuild(s => s
                .SetFile(OneForAllDockerFile)
                .AddBuildArg($"RUNTIME_IMAGE={GetRuntimeImage(proj)}")
                .AddBuildArg($"PROJECT_NAME={projectName}")
                .AddBuildArg($"BUILD_ID={BuildId}")
                .SetTag($"{GetProjectDockerImageName(proj)}:{BuildId.ToLower()}")
                .SetPath(publishedPath)
                .EnableForceRm());
             
           
        }
    }

    protected string GetRuntimeImage(string proj) => IsWebService(proj) ? WebservicesRuntimeDockerImage : AppsRuntimeDockerImage;

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
                            .SetConfiguration(Configuration)
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

    protected virtual string[] GetAllProjects()
    {
        var projects = GetApplicationProjects()
            .Concat(GetTestsProjects())
            .Concat(GetNugetPackageProjects())
            .Distinct()
            .OrderBy(s => s)
            .ToArray();

        return projects;
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

    protected virtual bool IsRunable(string proj) => IsWebService(proj) || IsStandardApp(proj);
    protected virtual bool IsStandardApp(string proj) => proj.EndsWith(".App.csproj", StringComparison.OrdinalIgnoreCase);
    protected virtual bool IsWebService(string proj) => proj.EndsWith(".RestAPI.csproj", StringComparison.OrdinalIgnoreCase);

    protected virtual string[] GetNugetPackageProjects()
    {
        var result = GlobFiles(SourceDirectory, "**/*.csproj").NotEmpty().OrderBy(p => p);
        return result.ToArray();
    }

    protected virtual void ShowInformations()
    {
        Info($"Host : {Host}");
        //Info($"Branch : {Branch}");
        //Info($"BuildId : {BuildId}");
        //Info($"BuildNumber: {BuildNumber}");
        Info($"RootDirectory : {RootDirectory}");
        Info($"ArtifactsDirectory : {ArtifactsDirectory}");
        //Info($"TestsOuputDirectory : {TestsOuputDirectory}");
        Info($"BuildProjectDirectory : {BuildProjectDirectory}");
        //Info($"NukeBuildProjectDirectory  : {NukeBuildProjectDirectory}");
        Info($"BuildAssemblyDirectory : {BuildAssemblyDirectory}");
        //Info($"DockerRegistryServer : {DockerRegistryServer ?? "-none-"}");
    }

    protected void PublishApplications(params string[] projects)
    {
        foreach (var proj in projects)
        {
            var projectName = Path.GetFileNameWithoutExtension(proj);
            var outputPath = ArtifactsDirectory / projectName;

            Info($"Publishing {projectName} in {outputPath}");

            DotNetRestore(s => s
                .SetProjectFile(proj));

            DotNetPublish(s => s
                .SetConfiguration(Configuration)
                .EnableNoRestore()
                .SetProject(proj)
                .SetOutput(outputPath)
                );

        }
    }


    readonly Dictionary<string, string> _kubeEnvironmentVariables = new Dictionary<string, string>();

    private T HelmEnvVars<T>(T kubeSettings) where T : HelmToolSettings => EnvVars(kubeSettings, _kubeEnvironmentVariables);

    private T EnvVars<T>(T settings, Dictionary<string, string> envVars)
        where T : ToolSettings
    {
        foreach (var v in envVars)
        {
            settings = settings.SetEnvironmentVariable(v.Key, v.Value);
        }
        return settings;
    }

    private void InstallNamespace(string group)
    {
        HelmInstall($"{group}-namespace", HelmChartsDirectory / "namespace", group, "default", true);
    }

    private void InstallGroup(string group, string groupFile)
    {

        HelmInstall(
            $"{group}-group-config", HelmChartsDirectory / "group",
           group, $"{group}-namespace",
            configurator: s =>
            {
                s = s.AddValues(groupFile);
                return s;
            });
    }

    private IReadOnlyCollection<Output> HelmInstall(string appName, string chart, string group, string @namespace, bool isNamespace = false, Configure<HelmUpgradeSettings> configurator = null)
    {

       // var groupConfig = ;
        var appConfig = BuildDirectory / "configs" / "config.app.yaml";


        Console.WriteLine($"{appName} {chart} {group} {@namespace} {isNamespace}");

        return HelmUpgrade(helmUpgradeSettings =>
        {
            helmUpgradeSettings = HelmEnvVars(helmUpgradeSettings)
                .EnableInstall()
                // .EnableForce()
                .SetRelease(appName)
                .SetChart(chart)
                .AddSet("group", group)
                .AddSet("app", appName)
                .SetNamespace(@namespace)
                //.SetRecreatePods(true)
                .AddValues(appConfig);

            if (!isNamespace)
            {
            
                helmUpgradeSettings = helmUpgradeSettings.AddSet("image.tag", BuildId.ToLower());
                helmUpgradeSettings = helmUpgradeSettings.AddSet("image.repository", DockerRegistryServer);
                helmUpgradeSettings = helmUpgradeSettings.AddSet("image.branch", Branch);
            }

            return helmUpgradeSettings;

        });
    }

    //protected void InstallGroup(string product, string tenant, string group, string env, string valuesFile = null)
    //{
    //    valuesFile = valuesFile ?? ConfigsDirectory / product / env / "groups" / group / "group.yaml";
    //    HelmInstall(
    //        $"{Namespace(product, tenant, group)}-group-config", HelmChartsDirectory / "bz-group",
    //        product, tenant, group, env: env,
    //        configurator: s =>
    //        {
    //            s = s.AddValues(valuesFile);
    //            return s;
    //        });
    //}





}


