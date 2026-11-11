// Purpose: Transcribe a bounded PCM utterance through the local Foundry speech session.
// Demo role: Share the final-transcript path between the console sample and the game.
// Requirements: A loaded English speech model and ordered, 16-kHz mono PCM frames.
// Expected result: The final transcript and any language metadata reported by the model.
using System.Text;
using Microsoft.AI.Foundry.Local;

namespace SpeechDemo;

public sealed record LocalSpeechResult(string Transcript, string? Language);

public static class LocalSpeechRecognizer
{
    private const int SampleRate = 16000;

    public static async Task<LocalSpeechResult> TranscribeFramesAsync(
        AudioSession session,
        IReadOnlyList<byte[]> frames,
        CancellationToken cancellationToken,
        Action<string>? onStreamingUpdate = null)
    {
        if (frames.Count == 0)
        {
            throw new InvalidDataException("No microphone audio was captured.");
        }

        // Step 1: Describe the PCM format and connect a bounded queue to the loaded model.
        using var audioQueue = new ItemQueue();
        using var request = new Request();
        request.AddItem(AudioItem.CreateFormatDescriptor("pcm", SampleRate, 1));
        request.AddItem(audioQueue, takeOwnership: false);
        await using var stream = session.ProcessStreamingRequestAsync(request, cancellationToken);

        // Step 2: Stream every captured frame while collecting model updates.
        var inputTask = ForwardFramesAsync(frames, audioQueue, cancellationToken);
        var responseTask = ReadFinalResponseAsync(stream, cancellationToken, onStreamingUpdate);
        await Task.WhenAll(inputTask, responseTask);
        return await responseTask;
    }

    private static async Task ForwardFramesAsync(
        IReadOnlyList<byte[]> frames,
        ItemQueue audioQueue,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            foreach (var frame in frames)
            {
                if (frame.Length == 0 || frame.Length % 2 != 0)
                {
                    throw new InvalidDataException("PCM input must contain whole 16-bit samples.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                using var audioItem = BytesItem.CreateOwned(frame);
                audioQueue.Push(audioItem);
            }
        }
        finally
        {
            // Step 3: Mark end-of-input so the model can produce its final transcript.
            audioQueue.MarkFinished();
        }
    }

    private static async Task<LocalSpeechResult> ReadFinalResponseAsync(
        StreamingResponse stream,
        CancellationToken cancellationToken,
        Action<string>? onStreamingUpdate)
    {
        var streamedTokens = new StringBuilder();

        await foreach (var item in stream.WithCancellation(cancellationToken))
        {
            using (item)
            {
                if (item is not SpeechSegmentItem segment)
                {
                    throw new InvalidDataException(
                        $"Unexpected streaming response item: {item.GetType().Name}.");
                }

                switch (segment.Kind)
                {
                    case SpeechSegmentKind.Partial:
                    case SpeechSegmentKind.Final:
                        onStreamingUpdate?.Invoke(segment.Text);
                        break;
                    case SpeechSegmentKind.None:
                        streamedTokens.Append(segment.Text);
                        onStreamingUpdate?.Invoke(streamedTokens.ToString());
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Unexpected speech segment kind '{segment.Kind}'.");
                }
            }
        }

        // Step 4: Read the aggregate result only after the model has finished streaming.
        using var finalResponse = await stream.FinalResponse;
        if (finalResponse.FinishReason != FinishReason.Stop)
        {
            throw new InvalidDataException(
                $"Speech recognition ended with {finalResponse.FinishReason}, not Stop.");
        }

        var finalResult = finalResponse.OfType<SpeechResultItem>().SingleOrDefault()
            ?? throw new InvalidDataException("The final response did not contain a speech result.");

        return new LocalSpeechResult(finalResult.Text, finalResult.Language);
    }
}
