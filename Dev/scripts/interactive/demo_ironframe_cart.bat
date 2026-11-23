@echo off
rem Clean-room synthetic demo launcher for ironframe-outerfortress-cart-01 (--demo-cart, zero retail ROMs).
setlocal
set "ROOT=%~dp0..\.."
set "EXE=%ROOT%\reCart.exe"
if exist "%EXE%" (
    "%EXE%" --demo-cart %*
    exit /b %ERRORLEVEL%
)
dotnet run --project "%ROOT%\src\reCart.Player\reCart.Player.csproj" -c Release -- --demo-cart %*
exit /b %ERRORLEVEL%
