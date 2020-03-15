
REM -----
REM change directory to this file's path
REM -----
CD /d %~dp0

REM -----
REM which environment are we connecting to
REM -----
echo %1
SET KUBEFILE=%1
SET KUBEPORT=8002

REM -----
REM set variables
REM -----
set KUBECONFIG=%CD%\kube\%KUBEFILE%
echo %KUBECONFIG%

REM -----
REM test tje config and confirm cluster-info 
REM -----
kubectl cluster-info

REM -----
REM open web page
REM -----
REM start http://localhost:%KUBEPORT%/api/v1/namespaces/kubernetes-dashboard/services/https:kubernetes-dashboard:/proxy/
REM start http://localhost:%KUBEPORT%/api/v1/namespaces/kube-system/services/kubernetes-dashboard/proxy
REM start http://localhost:%KUBEPORT%/api/v1/namespaces/kube-system/services/https:kubernetes-dashboard:/proxy/#!/overview?namespace=_all
REM start     http://localhost:%KUBEPORT%/api/v1/namespaces/kube-system/services/https:kubernetes-dashboard:/proxy/#!/overview?namespace=_all
start http://localhost:%KUBEPORT%/api/v1/namespaces/kubernetes-dashboard/services/https:kubernetes-dashboard:/proxy/

REM start the proxy
REM -----
kubectl proxy --port=%KUBEPORT%

REM -----
REM pause
REM -----
pause
