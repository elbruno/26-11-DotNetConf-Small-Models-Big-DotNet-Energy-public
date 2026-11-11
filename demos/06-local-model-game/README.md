# Demo 6: Local-model game

A four-wave side-scroller for the **Small Models, Big .NET Energy** session. Ollama's local Decisions model selects from bounded movement actions; C# owns movement, combat, collisions, pickups, score, and rendering. Voice commands use a separate local Foundry speech model.

**Status:** This is a review build, not an accepted or recording-ready demo. It has not been accepted through an interactive user run. Interactive rendering and input, the microphone flow, and a combined voice-and-Decisions session remain unverified.

## Requirements

- Windows x64 and the .NET SDK pinned by [`../global.json`](../global.json) (`11.0.100`, latest patch).
- A desktop graphics driver for the MonoGame window.
- Ollama running locally with `nimble:latest` and its Decisions capability for model control. Press `Tab` on the title screen to use keyboard control instead.
- Optional voice control requires Foundry Local and the `nemotron-speech-streaming-en-0.6b-generic-cpu:3` model cached for the `SmallModelsSpaceGame` app, plus a Windows microphone.

The microphone remains off until explicitly armed with `V`. Model weights are not included, and the game does not download them during startup.

## Run

From this directory:

```powershell
dotnet run
```

The game starts in local Decisions mode. To play with the keyboard, press `Tab` on the title screen before `Enter`.

## Controls

| Input | Behavior |
|---|---|
| `Enter` | Start, resume, or play again. |
| `P` | Pause and preserve the current mission. |
| `Escape` | Stop the mission and return to the title screen. |
| `Tab` | Switch between local Decisions and keyboard control. |
| Arrow keys or `WASD` | Move the ship in keyboard mode. The ship fires automatically. |
| `V` | Arm a short microphone capture. Press `V` again to submit early; capture also stops after three seconds. |
| `Q` | Exit the application. |

Voice control accepts only exact `START`, `PAUSE`, and `STOP` transcripts, with surrounding whitespace allowed. Other utterances are rejected. Audio remains in memory and is not saved to a file.

## Game loop

- Four bug types have different movement patterns: Null bugs blink, Race bugs change lanes, Leak bugs grow while slowing down, and Off-by-one bugs jump lanes.
- Defeating bugs reveals the boss, The Segfault. Four boss waves complete the mission.
- Power-ups restore hull, add a shield, increase speed, or multiply score. A helper drone joins in Wave 2.
- The ship stays on the left side and fires automatically. The local model receives the game objective and bounded legal movement options; the C# game loop owns simulation and combat.

## Artwork and brand

The 32 sprites and [contact sheet](assets/contact-sheet.png) were generated for this demo using the official [.NET brand kit](https://github.com/dotnet/brand) as visual reference. The brand repository licenses its illustrations under [CC0 1.0](https://github.com/dotnet/brand/blob/main/LICENSE) and directs users to the [.NET brand and logo guidance](https://github.com/dotnet/brand/blob/main/README.md). These generated sprites are not official Microsoft/.NET artwork. The root [MIT license](../../LICENSE) applies to original demo code and documentation, not to third-party marks or other materials.
