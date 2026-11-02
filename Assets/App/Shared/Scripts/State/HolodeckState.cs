namespace Holodeck.State
{
    public enum HolodeckState
    {
        Idle = 0,
        ListeningForCommand = 1,
        Interpreting = 2,
        Generating = 3,
        Ready = 4,
        Error = 5
    }
}
