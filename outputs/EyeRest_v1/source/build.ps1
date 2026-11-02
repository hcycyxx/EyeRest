$ErrorActionPreference = 'Stop'
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'EyeRest_v1.exe'
if (Test-Path -LiteralPath $target) { throw 'Target already exists. Build into a new version folder to preserve the previous executable.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap(32,32)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = 'AntiAlias'
$graphics.Clear([System.Drawing.Color]::FromArgb(30,91,75))
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White,2)
$graphics.DrawArc($pen,5,7,22,18,200,140)
$graphics.DrawArc($pen,5,7,22,18,20,140)
$graphics.FillEllipse([System.Drawing.Brushes]::White,13,13,6,6)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$iconPath = Join-Path $PSScriptRoot 'EyeRest.ico'
$stream = [System.IO.File]::Create($iconPath)
$icon.Save($stream)
$stream.Dispose(); $icon.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
$manifestPath = Join-Path $PSScriptRoot 'app.manifest'
$sourcePath = Join-Path $PSScriptRoot 'EyeRest.cs'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$target" "/win32icon:$iconPath" "/win32manifest:$manifestPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Write-Output $target
