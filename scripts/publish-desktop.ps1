# Собирает desktop-версию в один файл artifacts/desktop/MovieAG.exe.
# Нужны только .NET SDK 10 и Node 24; запускать из любой папки.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts\desktop'

function Invoke-Step([string]$Title, [scriptblock]$Command) {
    Write-Host "==> $Title"
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Шаг «$Title» завершился с кодом $LASTEXITCODE" }
}

Push-Location (Join-Path $root 'client')
try {
    Invoke-Step 'Установка пакетов клиента' { npm ci }
    Invoke-Step 'Сборка клиента' { npm run build }
}
finally {
    Pop-Location
}

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
Invoke-Step 'Сборка MovieAG.exe' {
    dotnet publish (Join-Path $root 'src\MovieAG.Desktop') -c Release -o $output
}

$exe = Get-Item (Join-Path $output 'MovieAG.exe')
Write-Host ("Готово: {0} ({1:N0} МБ)" -f $exe.FullName, ($exe.Length / 1MB))
