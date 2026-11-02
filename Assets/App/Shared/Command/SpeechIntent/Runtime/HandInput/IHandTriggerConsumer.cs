namespace SpeechIntent.HandInput
{
    public interface IHandTriggerConsumer
    {
        bool TryConsumeHandTrigger(HandSemanticEvent evt);
    }
}
