namespace SpeechIntent.HandInput
{
    public interface IHandGestureSource
    {
        bool TryGetSnapshot(HandedInputSide side, out HandGestureSnapshot snapshot);
    }
}
