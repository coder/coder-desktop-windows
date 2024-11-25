namespace Coder.Desktop.Rpc.Proto;

/// <summary>
///     Represents a role that either side of the connection can fulfil.
/// </summary>
public sealed class RpcRole
{
    public const string Manager = "manager";
    public const string Tunnel = "tunnel";

    public RpcRole(string role)
    {
        if (role != Manager && role != Tunnel) throw new ArgumentException($"Unknown role '{role}'");

        Role = role;
    }

    private string Role { get; }

    public override string ToString()
    {
        return Role;
    }

    #region SpeakerRole equality

    public static bool operator ==(RpcRole a, RpcRole b)
    {
        return a.Equals(b);
    }

    public static bool operator !=(RpcRole a, RpcRole b)
    {
        return !a.Equals(b);
    }

    private bool Equals(RpcRole other)
    {
        return Role == other.Role;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((RpcRole)obj);
    }

    public override int GetHashCode()
    {
        return Role.GetHashCode();
    }

    #endregion
}
