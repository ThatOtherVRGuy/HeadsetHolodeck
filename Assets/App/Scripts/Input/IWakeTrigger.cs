using System;

namespace Holodeck.Input
{
    public interface IWakeTrigger
    {
        event Action WakeTriggered;

        bool IsTriggerEnabled { get; }

        void EnableTrigger();
        void DisableTrigger();
    }
}
