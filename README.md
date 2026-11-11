# Small Models, Big .NET Energy

This is an audience-facing source release for the .NET Conf 2026 session **Small Models, Big .NET Energy**. The demos show local speech transcription, text cleanup, bounded decision routing, and a local-model game with .NET. The final session materials are listed on the [.NET Conf agenda](https://dotnet.microsoft.com/en-us/conf/2026/agenda); watch the session on the [.NET YouTube channel](https://www.youtube.com/watch?v=ongDevKLRxM).

## Demos

| Folder | What it demonstrates |
| --- | --- |
| [`demos/01-foundry-speech-to-text`](demos/01-foundry-speech-to-text/) | Transcribes a generated synthetic WAV locally with Foundry Local. |
| [`demos/02-foundry-live-speech-to-text`](demos/02-foundry-live-speech-to-text/) | Transcribes microphone audio locally with Foundry Local. It has not been live-verified; get fresh approval before capturing microphone audio. |
| [`demos/03-speech-s1mini-library-cleanup`](demos/03-speech-s1mini-library-cleanup/) | Sends the local speech transcript to the ElBruno.S1Mini text-cleanup library. |
| [`demos/04-speech-s1mini-foundry-local`](demos/04-speech-s1mini-foundry-local/) | Runs speech transcription and then text cleanup through Foundry Local. |
| [`demos/05-decisions-ollama`](demos/05-decisions-ollama/) | Sends one bounded choice request to the local Ollama Decisions endpoint and reports its distribution and latency. |
| [`demos/06-local-model-game`](demos/06-local-model-game/) | A review-build side-scroller where a local Decisions model selects bounded movement actions and C# controls the game simulation. |

Each folder contains an independent .NET project. The shared [`demos/tools/PrepareSpeechModel`](demos/tools/PrepareSpeechModel/) utility prepares the Foundry Local speech model used by Demos 1-4. Demos 1, 3, and 4 include a helper to generate a synthetic WAV locally; no recorded audio or model weights are included. Demo 6's [contact sheet](demos/06-local-model-game/assets/contact-sheet.png) previews its generated sprites.

### Demo 6 screenshot

[![Game title screen for Small Models, Big .NET Energy, with a rocket and Local Mission status panel](screenshots/small-models-game.png)](screenshots/small-models-game.png)

## Slides

The confirmed final v09 presentation is available as a [slide-only PDF](slides/Small-Models-Big-DotNet-Energy-v09.pdf). The editable PowerPoint and speaker notes are not included.

## Requirements and running

- Windows x64 and the .NET SDK selected by [`demos/global.json`](demos/global.json), version 11.0.100 with the latest patch.
- Demos 1-4 require Foundry Local and their required model(s) cached locally before running. Demo 3 also requires the S1-Mini INT4 weights in the ElBruno.S1Mini cache. Demo 4 additionally requires those cached weights to be staged with its `Stage-FoundryS1Mini.ps1` helper.
- Demo 5 requires Ollama 0.35.0 or later running locally, with `nimble:latest` installed and its Decisions capability available.
- Demo 6 requires Windows desktop graphics and local Ollama with `nimble:latest` for model control. Voice commands are optional and require Foundry Local, the speech model cached for the `SmallModelsSpaceGame` app, and a microphone.

Run from the demo's folder so the SDK version in `demos/global.json` is selected. For Demo 1:

```powershell
Set-Location demos\01-foundry-speech-to-text
dotnet restore --source https://api.nuget.org/v3/index.json
.\tools\New-SyntheticFixture.ps1
dotnet run
```

For Demos 3 and 4, run the fixture helper from that demo's folder before the project. For Demo 4, also stage the already-cached S1-Mini package with `tools\Stage-FoundryS1Mini.ps1` from its folder. Demo 2 opens the microphone; do not capture audio without fresh approval. Demo 5 can be run directly after its local Ollama prerequisites are met. Run Demo 6 with `dotnet run` from its folder; see its README for controls and optional voice setup. The demos use local inference and do not silently fall back to a cloud service. Generated fixtures, model weights, and other large runtime assets are intentionally not committed.

## Pending demo verification

Demo 6 is included as a review build, but has not been accepted through an interactive user run. Interactive rendering, the combined voice-and-Decisions flow, and recording-machine readiness remain unverified.

## Attribution and reuse

- Demo 1 uses original code informed by the Foundry Local concepts in the [.NET Blog article](https://devblogs.microsoft.com/dotnet/foundry-local-live-speech-to-text-csharp/). Its upstream [NVIDIA model card](https://huggingface.co/nvidia/nemotron-speech-streaming-en-0.6b) lists the NVIDIA Open Model License Agreement; Foundry Local separately reports MIT for its packaged variant. The model's applicable terms govern use of separately acquired weights.
- Demo 2's microphone capture and live-print pattern follows the author's public [`11-foundrylocal-live-transcription` sample](https://github.com/elbruno/26-07-15-AINET-LocalAI-MAF/blob/main/samples/11-foundrylocal-live-transcription/Program.cs), as well as the .NET Blog article linked above.
- Demo 4's S1-Mini conversion is based on [`elbruno/s1-mini-onnx`](https://huggingface.co/elbruno/s1-mini-onnx), an unofficial conversion of [`superwhisper/s1-mini`](https://huggingface.co/superwhisper/s1-mini). Review the upstream model terms, including its naming clause, before using separately acquired weights. The ElBruno.S1Mini library's MIT license is separate from the model terms.
- Demo 6's sprites were generated for the game using the official [.NET brand kit](https://github.com/dotnet/brand) as visual reference. The brand repository licenses its illustrations under [CC0 1.0](https://github.com/dotnet/brand/blob/main/LICENSE); follow its [.NET brand and logo guidance](https://github.com/dotnet/brand/blob/main/README.md). The sprites are not official Microsoft/.NET artwork, and this notice does not relicense .NET marks or imply endorsement.

This release does not include model weights. The root [MIT license](LICENSE) applies to original demo code and documentation authored by El Bruno. Third-party models, packages, branding, and other materials remain subject to their own applicable terms and notices.
