namespace SpeechIntent.Behaviors
{
    public sealed class BehaviorCommandResult
    {
        public bool success;
        public string message = "";
        public MissingCapabilityReport missingCapability;

        public static BehaviorCommandResult Success(string message)
        {
            return new BehaviorCommandResult
            {
                success = true,
                message = message ?? "",
                missingCapability = null
            };
        }

        public static BehaviorCommandResult Failure(string message)
        {
            return new BehaviorCommandResult
            {
                success = false,
                message = message ?? "",
                missingCapability = null
            };
        }

        public static BehaviorCommandResult Missing(MissingCapabilityReport report)
        {
            string message = report != null ? report.ToUserMessage() : "A required capability is not available.";
            return new BehaviorCommandResult
            {
                success = false,
                message = message,
                missingCapability = report
            };
        }
    }
}
