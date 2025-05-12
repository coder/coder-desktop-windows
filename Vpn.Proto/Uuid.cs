using Coder.Desktop.CoderSdk;

namespace Coder.Desktop.Vpn.Proto;

public partial class Workspace
{
    public Uuid ParseId()
    {
        return new Uuid(Id.Span);
    }
}

public partial class Agent
{
    public Uuid ParseId()
    {
        return new Uuid(Id.Span);
    }

    public Uuid ParseWorkspaceId()
    {
        return new Uuid(WorkspaceId.Span);
    }
}
