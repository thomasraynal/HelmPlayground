SET KUBEFILE=C:\Users\ThomasRAYNAL\.kube\config

echo %KUBEFILE%

nuke Deploy -DockerRegistryServer registry.hub.docker.com -BuildId %1 -BuildNumber %1