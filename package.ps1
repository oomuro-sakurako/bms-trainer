# 配布用フォルダと zip を作る。
#   dist\bmstrainer-v<バージョン>\      … 配布するフォルダ（exe・説明書・ライセンス・ソース）
#   dist\bmstrainer-v<バージョン>.zip   … 上のフォルダを zip にしたもの（-NoZip で作らない）
# 使い方: powershell -ExecutionPolicy Bypass -File package.ps1 [-NoZip]

param([switch]$NoZip)
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$root = Split-Path $project
$version = ([xml](Get-Content "$project\BmsTrainer.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "bmstrainer-v$version"
$out = Join-Path $root "dist\$name"
$zip = Join-Path $root "dist\$name.zip"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

dotnet publish "$project\BmsTrainer.csproj" -c Release -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish に失敗しました" }

Copy-Item "$project\README.txt", "$project\LICENSE.txt", "$project\THIRD-PARTY-NOTICES.txt" $out
# 英語・韓国語の説明書は GitHub 用の README をテキストとして同梱する
Copy-Item "$project\README.en.md" "$out\README_en.txt"
Copy-Item "$project\README.ko.md" "$out\README_ko.txt"
Copy-Item "$project\licenses" $out -Recurse

# GPL に従い、ソースコードを同梱する（bin / obj は除く）
$src = Join-Path $env:TEMP "bmstrainer-src-$version"
if (Test-Path $src) { Remove-Item $src -Recurse -Force }
New-Item -ItemType Directory $src | Out-Null
Get-ChildItem $project -Exclude bin, obj, .vs, *.user, *.log | Copy-Item -Destination $src -Recurse
New-Item -ItemType Directory "$out\source" | Out-Null
Compress-Archive -Path "$src\*" -DestinationPath "$out\source\bmstrainer-src-v$version.zip"
Remove-Item $src -Recurse -Force

if ($NoZip) {
    Write-Host "作成しました: $out"
} else {
    Compress-Archive -Path $out -DestinationPath $zip
    Write-Host "作成しました: $zip"
}
