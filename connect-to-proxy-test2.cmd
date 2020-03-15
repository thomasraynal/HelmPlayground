echo off

REM -----
REM change directory to this file's path
REM -----
CD /d %~dp0

call connect-to-proxy.cmd test2
