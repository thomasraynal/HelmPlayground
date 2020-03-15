SET KUBEFILE=.\build\kube\test2

echo %KUBEFILE%

nuke Deploy -DockerRegistryServer registry.hub.docker.com -DockerRegistryUserName thomasraynal  -DockerRegistryPassword 1a586422-cf94-47fd-b253-772ee5f612da -BuildId %1 -DomainToBeDeployed %2