<#
Purpose: Stage a local S1-Mini ONNX package for the optional Foundry Local demo mode.
Output: An ignored artifacts directory with a non-thinking template override.
The cached source model is never modified, downloaded, or copied into tracked files.
#>
[CmdletBinding()]
param(
    [string]$SourceModelPath = (Join-Path $env:LOCALAPPDATA 'ElBruno\S1Mini\models\elbruno_s1-mini-onnx\int4'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\foundry-s1mini-cache')
)

$ErrorActionPreference = 'Stop'

$requiredFiles = @(
    'chat_template.jinja',
    'genai_config.json',
    'model.onnx',
    'model.onnx.data',
    'tokenizer_config.json',
    'tokenizer.json'
)
$source = (Resolve-Path -LiteralPath $SourceModelPath).Path
$target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$modelTarget = Join-Path $target 's1-mini-int4'
$thinkingOverride = '{%- set enable_thinking = false -%}'
$sourceTemplatePath = Join-Path $source 'chat_template.jinja'
$sourceTemplate = [System.IO.File]::ReadAllText($sourceTemplatePath)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $file) -PathType Leaf)) {
        throw "The cached S1-Mini package is missing '$file'. No model files were staged."
    }
}

if (-not $sourceTemplate.Contains('enable_thinking is defined and enable_thinking is false')) {
    throw 'The cached chat template does not support the expected non-thinking override.'
}

$expectedTemplate = "$thinkingOverride`r`n$sourceTemplate"
if (Test-Path -LiteralPath $modelTarget) {
    foreach ($file in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $modelTarget $file) -PathType Leaf)) {
            throw "The existing staging directory is incomplete. It was not changed: '$modelTarget'."
        }
    }

    $stagedTemplate = [System.IO.File]::ReadAllText(
        (Join-Path $modelTarget 'chat_template.jinja')
    )
    if ($stagedTemplate -cne $expectedTemplate) {
        throw "The existing staging template differs from the expected isolated override. It was not changed: '$modelTarget'."
    }

    # Step 1: Verify the existing staging copy without overwriting or repairing it.
    foreach ($file in $requiredFiles | Where-Object { $_ -ne 'chat_template.jinja' }) {
        $sourceHash = (Get-FileHash -LiteralPath (Join-Path $source $file) -Algorithm SHA256).Hash
        $stagedHash = (Get-FileHash -LiteralPath (Join-Path $modelTarget $file) -Algorithm SHA256).Hash
        if ($sourceHash -ne $stagedHash) {
            throw "The staged file '$file' differs from the cached source package."
        }
    }

    Write-Output "Verified existing staged S1-Mini package: $modelTarget"
    return
}

if (Test-Path -LiteralPath $target) {
    throw "The output directory already exists and was not changed: '$target'."
}

# Step 1: Copy only the six model files needed by the local ONNX package.
[System.IO.Directory]::CreateDirectory($modelTarget) | Out-Null
foreach ($file in $requiredFiles) {
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $modelTarget $file)
}

# Step 2: Make the staged copy use the model-card's non-thinking prompt format.
$templatePath = Join-Path $modelTarget 'chat_template.jinja'
[System.IO.File]::WriteAllText(
    $templatePath,
    $expectedTemplate,
    [System.Text.UTF8Encoding]::new($false)
)

# Step 3: Verify copied model assets and the isolated template change.
foreach ($file in $requiredFiles | Where-Object { $_ -ne 'chat_template.jinja' }) {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $source $file) -Algorithm SHA256).Hash
    $stagedHash = (Get-FileHash -LiteralPath (Join-Path $modelTarget $file) -Algorithm SHA256).Hash
    if ($sourceHash -ne $stagedHash) {
        throw "The staged file '$file' did not match its cached source."
    }
}

Write-Output "Staged S1-Mini package for Foundry Local: $modelTarget"
