# Собирает архивы модов для сайта в site\packages\<id>.zip.
# Внутри каждого архива — папка scripts\ (её распаковывают прямо в папку GTAIV) и УСТАНОВКА.txt.
# Запуск из папки site:  powershell -ExecutionPolicy Bypass -File tools\build-packages.ps1
# При первом запуске сервера архивы из packages\ регистрируются как версия 1.0.0.
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\..\scripts')
)
$ErrorActionPreference = 'Stop'
$Source = (Resolve-Path $Source).Path
$out = Join-Path $PSScriptRoot '..\packages'
New-Item -ItemType Directory -Force $out | Out-Null
$out = (Resolve-Path $out).Path

$mods = [ordered]@{
    'car-spawner'  = @('CarSpawner.net.cs', 'CarSpawner')
    'weapon-menu'  = @('WeaponMenu.net.cs', 'WeaponMenu')
    'npc-spawner'  = @('NpcSpawner.net.cs', 'NpcSpawner')
    'fly-mode'     = @('FlyMode.net.cs')
    'mod-settings' = @('ModSettings.net.cs')
}
$all = @(); foreach ($k in $mods.Keys) { $all += $mods[$k] }
$mods['full-pack'] = $all

$readme = @'
LIBERTY CHAOS PACK — моды для GTA IV (Steam, Complete Edition 1.2.0.59)

1. Установи в папку игры (...\Grand Theft Auto IV\GTAIV):
   - Ultimate ASI Loader (dinput8.dll):  https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases
   - ScriptHookDotNet v1.7.1.8:           https://github.com/Tomasak/gta4_scripthookdotnet/releases
2. Распакуй ЭТОТ архив прямо в папку GTAIV — папка scripts\ должна оказаться рядом с GTAIV.exe.
3. В Windows 11 выключи «Интеллектуальное управление приложениями»
   (Безопасность Windows -> Управление приложениями и браузером), иначе dinput8.dll блокируется.
4. Запусти игру. Клавиши: F5 транспорт, F6 оружие, F7 NPC, F8 полёт, F9 настройки.

Ошибки модов пишутся в ScriptHookDotNet.log в папке игры.
'@

$tmpRoot = Join-Path $env:TEMP ('lcpack_' + [guid]::NewGuid().ToString('N'))
foreach ($id in $mods.Keys) {
    $stage = Join-Path $tmpRoot $id
    $scripts = Join-Path $stage 'scripts'
    New-Item -ItemType Directory -Force $scripts | Out-Null
    foreach ($item in $mods[$id]) {
        $src = Join-Path $Source $item
        if (-not (Test-Path $src)) { Write-Warning "$id : нет $item — пропускаю"; continue }
        Copy-Item $src $scripts -Recurse -Force
    }
    [IO.File]::WriteAllText((Join-Path $stage 'УСТАНОВКА.txt'), $readme, (New-Object Text.UTF8Encoding $true))
    $zip = Join-Path $out "$id.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    '{0,-14} {1,8:N0} KB' -f $id, ((Get-Item $zip).Length / 1KB)
}
Remove-Item $tmpRoot -Recurse -Force
"Готово: $out"
