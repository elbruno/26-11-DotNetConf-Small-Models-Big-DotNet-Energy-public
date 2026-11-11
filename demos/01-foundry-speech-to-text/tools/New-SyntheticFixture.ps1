<#
Purpose: Create a short, original English WAV fixture for local speech tests.
Output: An ignored artifacts file in 16 kHz, 16-bit, mono PCM format.
The fixture uses a local Windows SAPI voice and does not record a person.
#>
[CmdletBinding()]
param(
    [string]$VoiceName = 'Microsoft David Desktop',
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\fixture\english-cleanup-cases.wav'),
    [string]$SpokenText = 'So, um, I need to send the the report by Friday. No, wait, make that Thursday at three thirty p.m. Please update the project notes before the meeting.'
)

$ErrorActionPreference = 'Stop'

# Step 1: Resolve a fixed, local English voice so the fixture has clear provenance.
Add-Type -AssemblyName System.Speech
$synthesizer = [System.Speech.Synthesis.SpeechSynthesizer]::new()
try {
    $voice = $synthesizer.GetInstalledVoices() |
        Where-Object {
            $_.VoiceInfo.Name -eq $VoiceName -and
            $_.VoiceInfo.Culture.Name -eq 'en-US'
        } |
        Select-Object -First 1

    if (-not $voice) {
        throw "The en-US voice '$VoiceName' is not installed. Choose an installed en-US voice with -VoiceName."
    }

    $synthesizer.SelectVoice($VoiceName)

    # Step 2: Keep the spoken text explicit so each fixture has reproducible provenance.
    if ([string]::IsNullOrWhiteSpace($SpokenText)) {
        throw 'Provide non-empty English text with -SpokenText.'
    }

    # Step 3: Select the mono PCM format expected by the speech-model input.
    $audioFormat = [System.Speech.AudioFormat.SpeechAudioFormatInfo]::new(
        16000,
        [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
        [System.Speech.AudioFormat.AudioChannel]::Mono
    )

    # Step 4: Write the generated speech to a local artifact, never to a tracked source path.
    $destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
    if (Test-Path -LiteralPath $destination) {
        throw "The fixture already exists at '$destination'. Choose a new output path to avoid overwriting it."
    }

    $parentDirectory = [System.IO.Path]::GetDirectoryName($destination)
    [System.IO.Directory]::CreateDirectory($parentDirectory) | Out-Null
    $synthesizer.SetOutputToWaveFile($destination, $audioFormat)
    $synthesizer.Speak($SpokenText)
    $synthesizer.SetOutputToNull()

    Write-Output $destination
}
finally {
    $synthesizer.Dispose()
}
