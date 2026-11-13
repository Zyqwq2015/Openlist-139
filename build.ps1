# 需要 .NET Framework 4.x（Windows 10/11 自带）与系统自带的 csc.exe
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "未找到 .NET Framework 编译器 csc.exe"
}

$out = Join-Path $PSScriptRoot "Openlist-139.exe"

& $csc /nologo /target:winexe /codepage:65001 /optimize+ /out:"$out" `
    /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll `
    /resource:"src\web\index.html",web.index.html `
    /resource:"src\web\app.js",web.app.js `
    /resource:"src\web\style.css",web.style.css `
    "src\Program.cs"

if ($LASTEXITCODE -ne 0) { throw "构建失败" }

Write-Host "构建成功: $out" -ForegroundColor Green
