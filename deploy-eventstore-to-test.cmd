SET KUBEFILE=.\build\kube\test

echo %KUBEFILE%

nuke InstallEventStore -DockerRegistryServer registry.hub.docker.com -DockerRegistryUserName thomasraynal  -DockerRegistryPassword 1a586422-cf94-47fd-b253-772ee5f612da -BuildId 0 -DomainToBeDeployed eventstore