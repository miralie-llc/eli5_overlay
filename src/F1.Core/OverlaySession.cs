namespace F1.Core;

/// <summary>UI-independent ownership of one visible conversation and its cancellation generation.</summary>
public sealed class OverlaySession
{
    private long generation;
    public OverlayState State { get; private set; } = OverlayState.Hidden;
    public string Answer { get; private set; } = "";
    public long Begin() { Answer = ""; State = OverlayState.Thinking; return ++generation; }
    public void Show() => State = OverlayState.Ready;
    public void Hide() { ++generation; Answer = ""; State = OverlayState.Hidden; }
    public bool Apply(long request, AgentEvent value)
    {
        if (request != generation || State == OverlayState.Hidden) return false;
        switch (value.Kind)
        {
            case "text": Answer += value.Text; State = OverlayState.Answering; break;
            case "interaction": State = OverlayState.WaitingForInput; break;
            case "error": State = OverlayState.Error; break;
            case "complete": State = OverlayState.Ready; break;
        }
        return true;
    }
}
