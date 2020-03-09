SET KUBEFILE=C:\Users\ThomasRAYNAL\.kube\config

echo %KUBEFILE%

nuke Deploy -DockerRegistryServer registry.hub.docker.com -DockerRegistryUserName thomasraynal  -DockerRegistryPassword 1a586422-cf94-47fd-b253-772ee5f612da -BuildId %1 -BuildNumber %1