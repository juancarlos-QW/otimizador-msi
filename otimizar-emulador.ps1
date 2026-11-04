# Ajusta a instancia do MSI App Player usada pelo Free Fire e guarda os valores originais.
# Uso: otimizar-emulador.ps1 [-Simular] [-Restaurar]
param([switch]$Restaurar, [switch]$Simular)

$ErrorActionPreference = 'Stop'

$conf   = 'C:\ProgramData\BlueStacks_msi5\bluestacks.conf'
$estado = "$conf.antes-da-otimizacao.json"
$ifeo   = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\HD-Player.exe'
$perf   = "$ifeo\PerfOptions"
$utf8   = New-Object System.Text.UTF8Encoding($false)

# Pie64 e a instancia em que o log do emulador mostra o Free Fire rodando.
$ajustes = [ordered]@{
    'bst.instance.Pie64.cpus'    = '4'
    'bst.instance.Pie64.ram'     = '4096'
    'bst.instance.Pie64.max_fps' = '120'
}
$prioridadeAlta = 3

function Get-Padrao($chave) { '(?m)^' + [regex]::Escape($chave) + '="([^"]*)"' }

function Get-Valor($texto, $chave) {
    $m = [regex]::Match($texto, (Get-Padrao $chave))
    if ($m.Success) { $m.Groups[1].Value } else { $null }
}

function Set-Valor($texto, $chave, $valor) {
    $linha = ($chave + '="' + $valor + '"').Replace('$', '$$')
    [regex]::Replace($texto, (Get-Padrao $chave), $linha)
}

function Save-Conf($texto) {
    # A troca atomica evita deixar a configuracao pela metade se algo falhar.
    $tmp = "$conf.tmp"
    [IO.File]::WriteAllText($tmp, $texto, $utf8)
    [IO.File]::Replace($tmp, $conf, "$conf.bak")
}

function Assert-Admin {
    $id = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $id.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Execute como administrador.'
    }
}

function Assert-EmuladorFechado {
    # O emulador regrava o arquivo ao fechar e desfaria as mudancas.
    if (Get-Process -Name 'HD-Player', 'HD-MultiInstanceManager' -ErrorAction SilentlyContinue) {
        throw 'Feche o emulador e tente de novo.'
    }
}

$texto = [IO.File]::ReadAllText($conf, $utf8)

if ($Restaurar) {
    if (-not (Test-Path $estado)) { throw 'Nenhuma otimizacao para restaurar.' }
    $e = Get-Content $estado -Raw | ConvertFrom-Json

    foreach ($p in $e.Valores.PSObject.Properties) {
        Write-Host "$($p.Name): $(Get-Valor $texto $p.Name) -> $($p.Value)"
    }
    Write-Host "Prioridade do HD-Player: volta ao estado original"
    if ($Simular) { Write-Host 'Simulacao: nada foi alterado.'; return }

    Assert-Admin
    Assert-EmuladorFechado
    foreach ($p in $e.Valores.PSObject.Properties) {
        $texto = Set-Valor $texto $p.Name $p.Value
    }
    Save-Conf $texto

    if ($null -ne $e.PrioridadeAnterior) {
        New-ItemProperty -Path $perf -Name CpuPriorityClass -Value $e.PrioridadeAnterior -PropertyType DWord -Force | Out-Null
    } elseif (Test-Path $perf) {
        Remove-ItemProperty -Path $perf -Name CpuPriorityClass -ErrorAction SilentlyContinue
    }
    if (-not $e.PerfOptionsExistia -and (Test-Path $perf)) { Remove-Item $perf -Recurse }
    if (-not $e.IfeoExistia -and (Test-Path $ifeo)) { Remove-Item $ifeo -Recurse }

    Remove-Item $estado
    Write-Host 'Configuracao original restaurada.'
    return
}

foreach ($chave in $ajustes.Keys) {
    $atual = Get-Valor $texto $chave
    if ($null -eq $atual) { throw "Chave nao encontrada, nada foi alterado: $chave" }
    Write-Host "${chave}: $atual -> $($ajustes[$chave])"
}
Write-Host "Prioridade do HD-Player: Alta"
if ($Simular) { Write-Host 'Simulacao: nada foi alterado.'; return }

Assert-Admin
Assert-EmuladorFechado

# Rodar de novo nao pode apagar os valores originais ja guardados.
if (-not (Test-Path $estado)) {
    $valores = [ordered]@{}
    foreach ($chave in $ajustes.Keys) { $valores[$chave] = Get-Valor $texto $chave }
    $anterior = $null
    if (Test-Path $perf) { $anterior = (Get-ItemProperty $perf).CpuPriorityClass }
    [ordered]@{
        Valores            = $valores
        PrioridadeAnterior = $anterior
        PerfOptionsExistia = [bool](Test-Path $perf)
        IfeoExistia        = [bool](Test-Path $ifeo)
    } | ConvertTo-Json | Set-Content -Path $estado -Encoding UTF8
}

foreach ($chave in $ajustes.Keys) { $texto = Set-Valor $texto $chave $ajustes[$chave] }
Save-Conf $texto

# New-Item -Force apagaria os valores de uma chave de registro que ja existe.
if (-not (Test-Path $perf)) { New-Item -Path $perf -Force | Out-Null }
New-ItemProperty -Path $perf -Name CpuPriorityClass -Value $prioridadeAlta -PropertyType DWord -Force | Out-Null

Write-Host 'Otimizacao aplicada. Vale na proxima vez que abrir o emulador.'
