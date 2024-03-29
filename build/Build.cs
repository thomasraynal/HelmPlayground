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

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
public class Build : NukeBuild
{

    [GitRepository]
    private readonly GitRepository GitRepository;

    private readonly Configuration Configuration = Configuration.Release;
    
    public string RuntimeDockerImage { get; set; } = "microsoft/dotnet:2.2-aspnetcore-runtime";

    [Required]
    [Parameter("Docker registry")]
    public string DockerRegistryServer;

    [Required]
    [Parameter("Docker registry user name")]
    public string DockerRegistryUserName;

    [Required]
    [Parameter("Docker registry password")]
    public string DockerRegistryPassword;

    [Required]
    [Parameter("Set the build Id.")]
    public string BuildId;

    [Required]
    [Parameter("Set the domain to be deployed")]
    public string DomainToBeDeployed;

    [Parameter("Set the applications to be deployed")]
    public string[] ApplicationsToBeDeployed;

    private AbsolutePath BuildDirectory => RootDirectory / "build";
    private AbsolutePath SourceDirectory => RootDirectory / "src";
    private AbsolutePath TestsDirectory => RootDirectory / "tests";
    private AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    private AbsolutePath ConfigsDirectory => BuildDirectory / "configs";
    private AbsolutePath HelmChartsDirectory => BuildDirectory / "helm" / "charts";
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

            foreach (var dir in SourceDirectory.GlobDirectories("**/bin", "**/obj")
                         .Concat(TestsDirectory.GlobDirectories("**/bin", "**/obj")))
                try
                {
                    if (!DirectoryExists(dir))
                    {
                        Warn($"Not existing directory : {dir}");
                        continue;
                    }

                    DeleteDirectory(dir);
                }
                catch
                {
                }

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
            foreach(var project in GetApplicationProjects())
            {
                var appName = Path.GetFileName(project).Replace(".csproj", "");
                
                DeployApps(appName);

            }

        });



    public Target AddEventStore => _ => _
        .Executes(() =>
        {
            HelmRepoAdd(helmRepoAddSettings =>
           {

               return helmRepoAddSettings.SetName("eventstore")
                                         .SetUrl("https://eventstore.github.io/EventStore.Charts");

           });

            HelmRepoUpdate();

        });


    public Target InstallEventStore => _ => _
        .DependsOn(AddEventStore)
        .Executes(() =>
        {

            //var eventStoreNamespace = "eventstore";

            //InstallNamespace(eventStoreNamespace);

            HelmUpgrade(helmRepoUpgradeSettings =>
            {

                return helmRepoUpgradeSettings
                .EnableForce()
                .EnableInstall()
               // .SetNamespace(eventStoreNamespace)
                .SetChart("eventstore/eventstore")
                .SetRelease("eventstore");
                                                          
            });

        });




    private void DeployApps(params string[] appNames)
    {

        var lowerCaseAppGroup = DomainToBeDeployed.ToLower();

        HelmRepoUpdate();

        InstallNamespace(lowerCaseAppGroup);

        InstallDomain();

        (string app, string appName, string appShortName)[] apps =
            appNames
                .Select(appName => ($"{lowerCaseAppGroup}.{appName.ToLower()}", $"{appName.ToLower()}", $"{DomainToBeDeployed}.{appName.ToLower()}"))
                .ToArray();

        foreach (var app in apps)
        {
            HelmInstall(app.appName, HelmChartsDirectory / "api", DomainToBeDeployed, DomainToBeDeployed);
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

            Console.WriteLine(imageNameAndTagOnRegistry);

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
                .AddBuildArg($"RUNTIME_IMAGE={RuntimeDockerImage}")
                .AddBuildArg($"PROJECT_NAME={projectName}")
                .AddBuildArg($"BUILD_ID={BuildId}")
                .SetTag($"{GetProjectDockerImageName(proj)}:{BuildId.ToLower()}")
                .SetPath(publishedPath)
                .EnableForceRm());
           
        }
    }

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

    protected virtual string[] GetTestsProjects()
    {
        var result = GlobFiles(TestsDirectory, $"**/*.Tests.csproj").NotEmpty().OrderBy(p => p);
        return result.ToArray();
    }

    protected virtual string[] GetApplicationProjects(AbsolutePath directory = null)
    {
        directory = directory ?? SourceDirectory;

        var result = GlobFiles(directory, "**/*.App.csproj").NotEmpty().OrderBy(p => p);
        return result.ToArray();
    }
    protected virtual string[] GetNugetPackageProjects()
    {
        var result = GlobFiles(SourceDirectory, "**/*.csproj").NotEmpty().OrderBy(p => p);
        return result.ToArray();
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

    private void InstallNamespace(string group)
    {
        HelmInstall(group, HelmChartsDirectory / "namespace", group, "default", true);
    }

    private void InstallDomain()
    {
        HelmUpgrade(helmUpgradeSettings =>
       {
           return helmUpgradeSettings
                   .EnableInstall()
                   // .EnableForce()
                   .SetRelease($"{DomainToBeDeployed}-group-config")
                   .SetChart(HelmChartsDirectory / "group")
                   .AddSet("group", DomainToBeDeployed)
                   .SetNamespace(DomainToBeDeployed)
                   //.SetRecreatePods(true)
                   .AddValues(ConfigsDirectory / DomainToBeDeployed / "config.group.yaml");

       });
    }

    private IReadOnlyCollection<Output> HelmInstall(string appName, string chart, string group, string @namespace, bool isNamespace = false, Configure<HelmUpgradeSettings> configurator = null)
    {

        return HelmUpgrade(helmUpgradeSettings =>
        {
            helmUpgradeSettings = helmUpgradeSettings
                .EnableInstall()
                // .EnableForce()
                .SetRelease(appName)
                .SetChart(chart)
                .AddSet("group", group)
                .AddSet("app", appName)
                .SetNamespace(@namespace);
            //.SetRecreatePods(true)


            if (!isNamespace)
            {
                var appConfig = ConfigsDirectory / DomainToBeDeployed / appName / "config.app.yaml";

                helmUpgradeSettings = helmUpgradeSettings.AddSet("image.tag", BuildId.ToLower())
                                                         .AddSet("image.repository", $"{DockerRegistryServer}/{DockerRegistryUserName}")
                                                         .AddSet("image.branch", Branch)
                                                         .AddValues(appConfig);
            }

            return helmUpgradeSettings;

        });
    }






}


