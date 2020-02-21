using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.ProjectModel;
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
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Nuke.Common.IO;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{

    readonly Configuration Configuration = Configuration.Release;

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

    [GitRepository] readonly GitRepository GitRepository;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    protected AbsolutePath OneForAllDockerFile => BuildAssemblyDirectory / "docker" / "build.nuke.app.dockerfile";

    public static int Main() => Execute<Build>(x => x.Publish);

     Target Test => _ => _
            .DependsOn(Publish)
            .Executes(() =>
            {
                ExecuteTests(GetTestsProjects());
            });


    Target Clean => _ => _
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
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
        BuildContainers(applications.ToArray());
    });


    public virtual Target Push => _ => _
        .DependsOn(Package)
        .Executes(() =>
        {
            var applications = GetApplicationProjects();
            PushContainers(applications.ToArray());
        });


    public virtual Target CleanPackage => _ => _
        .After(Push)
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


    protected void PushContainers(string[] projects)
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


        }
    }

    protected string GetProjectDockerImageName(string project)
    {
        var prefix = GetProjectDockerPrefix(project);
        return $"{prefix}-{GitRepository.Branch.Replace("/", "")}".ToLower();
    }

    protected string GetProjectDockerTagName()
    {
        return BuildId.ToLower();
    }

    protected static string GetProjectDockerPrefix(string project)
    {
        return GetAppNameFromProject(project).ToLower();
    }

    protected static string GetAppNameFromProject(string project)
    {
        return Path.GetFileNameWithoutExtension(project);
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

            //var appConstsFile = GlobFiles(Path.GetDirectoryName(proj), "config.app.consts.yaml").FirstOrDefault();
            //if (appConstsFile == null)
            //{
            //    Warn("Skipped : no 'config.app.consts.yaml' found");
            //    continue;
            //}

            DockerBuild(s => s
                .SetFile(OneForAllDockerFile)
                .AddBuildArg($"RUNTIME_IMAGE={GetRuntimeImage(proj)}")
                .AddBuildArg($"PROJECT_NAME={projectName}")
                .AddBuildArg($"BUILD_ID={BuildId}")
                .SetTag($"{GetProjectDockerImageName(proj)}:{GetProjectDockerTagName()}")
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
                            //.SetResultsDirectory(TestsOuputDirectory)
                            //.SetLogger($"trx;LogFileName={projectName}.trx  ")
                            //.SetProperty("CollectCoverage", true)
                            //.SetProperty("CoverletOutputFormat", "opencover")
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

}


