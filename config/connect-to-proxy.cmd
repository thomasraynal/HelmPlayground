echo off

REM -----
REM change directory to this file's path
REM -----
CD /d %~dp0


REM -----
REM which environment are we connecting to
REM -----
echo %1
SET ENVFOLDER=%1
IF [%ENVFOLDER%]==[PROD] (
COLOR c0
SET KUBEFILE=prodconfig
SET KUBEPORT=8000
) ELSE (
COLOR e0
SET KUBEFILE=testconfig
SET KUBEPORT=8001
)


REM -----
REM set variables
REM -----
set KUBECONFIG=%CD%\.kube\%KUBEFILE%
REM echo %KUBECONFIG%



REM -----
REM test tje config and confirm cluster-info 
REM -----
kubectl cluster-info


REM -----
REM open web page
REM -----
start http://localhost:%KUBEPORT%/api/v1/namespaces/kube-system/services/kubernetes-dashboard/proxy


REM -----
REM start the proxy
REM -----
kubectl proxy --port=%KUBEPORT%

REM -----
REM pause
REM -----
pause

REM -----
REM reset color
REM -----
COLOR 0f
