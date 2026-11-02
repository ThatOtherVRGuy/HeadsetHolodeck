using System;

namespace SpeechIntent
{
    public enum ArchStatusLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    public readonly struct ArchStatusMessage
    {
        public ArchStatusMessage(string mode, string message, ArchStatusLevel level, DateTime timestampUtc)
        {
            this.mode = string.IsNullOrWhiteSpace(mode) ? "STATUS" : mode.Trim();
            this.message = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
            this.level = level;
            this.timestampUtc = timestampUtc;
        }

        public readonly string mode;
        public readonly string message;
        public readonly ArchStatusLevel level;
        public readonly DateTime timestampUtc;
    }

    public static class ArchStatusBus
    {
        public static event Action<ArchStatusMessage> StatusPosted;

        public static ArchStatusMessage LastMessage { get; private set; } =
            new ArchStatusMessage("READY", "Holodeck systems standing by.", ArchStatusLevel.Info, DateTime.UtcNow);

        public static void Post(string message, ArchStatusLevel level = ArchStatusLevel.Info, string mode = "STATUS")
        {
            LastMessage = new ArchStatusMessage(mode, message, level, DateTime.UtcNow);
            StatusPosted?.Invoke(LastMessage);
        }

        public static void Info(string message, string mode = "STATUS") => Post(message, ArchStatusLevel.Info, mode);
        public static void Success(string message, string mode = "READY") => Post(message, ArchStatusLevel.Success, mode);
        public static void Warning(string message, string mode = "WARNING") => Post(message, ArchStatusLevel.Warning, mode);
        public static void Error(string message, string mode = "ERROR") => Post(message, ArchStatusLevel.Error, mode);
    }
}
