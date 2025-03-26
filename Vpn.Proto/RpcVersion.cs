namespace Coder.Desktop.Vpn.Proto;

/// <summary>
///     A version of the RPC API. Can be compared other versions to determine compatibility between two peers.
/// </summary>
public class RpcVersion
{
    public static readonly RpcVersion Current = new(1, 1);

    public ulong Major { get; }
    public ulong Minor { get; }

    /// <param name="major">The major version of the peer</param>
    /// <param name="minor">The minor version of the peer</param>
    public RpcVersion(ulong major, ulong minor)
    {
        Major = major;
        Minor = minor;
    }

    /// <summary>
    ///     Parse a string in the format "major.minor" into an ApiVersion.
    /// </summary>
    /// <param name="versionString">Version string to parse</param>
    /// <returns>Parsed ApiVersion</returns>
    /// <exception cref="ArgumentException">The version string is invalid</exception>
    public static RpcVersion Parse(string versionString)
    {
        var parts = versionString.Split('.');
        if (parts.Length != 2) throw new ArgumentException($"Invalid version string '{versionString}'");

        try
        {
            var major = ulong.Parse(parts[0]);
            if (major == 0) throw new ArgumentException($"Invalid major version '{major}'");
            var minor = ulong.Parse(parts[1]);
            return new RpcVersion(major, minor);
        }
        catch (FormatException e)
        {
            throw new ArgumentException($"Invalid version string '{versionString}'", e);
        }
    }

    public override string ToString()
    {
        return $"{Major}.{Minor}";
    }

    /// <summary>
    ///     Returns the lowest version that is compatible with both this version and the other version. If no compatible
    ///     version is found, null is returned.
    /// </summary>
    /// <param name="other">Version to compare against</param>
    /// <returns>The highest compatible version</returns>
    public RpcVersion? IsCompatibleWith(RpcVersion other)
    {
        if (Major != other.Major)
            return null;

        // The lowest minor version from the two versions should be returned.
        return Minor < other.Minor ? this : other;
    }

    #region RpcVersion Equality

    public static bool operator ==(RpcVersion a, RpcVersion b)
    {
        return a.Equals(b);
    }

    public static bool operator !=(RpcVersion a, RpcVersion b)
    {
        return !a.Equals(b);
    }

    private bool Equals(RpcVersion other)
    {
        return Major == other.Major && Minor == other.Minor;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((RpcVersion)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor);
    }

    #endregion
}

public class RpcVersionList : List<RpcVersion>
{
    public static readonly RpcVersionList Current = new(RpcVersion.Current);

    public RpcVersionList(IEnumerable<RpcVersion> versions) : base(versions)
    {
    }

    public RpcVersionList(params RpcVersion[] versions) : base(versions)
    {
    }

    public static RpcVersionList Parse(string versions)
    {
        try
        {
            var l = new RpcVersionList(versions.Split(',').Select(RpcVersion.Parse));
            l.Validate();
            return l;
        }
        catch (Exception e)
        {
            throw new ArgumentException($"Invalid version list '{versions}'", e);
        }
    }

    public override string ToString()
    {
        return string.Join(",", this);
    }

    /// <summary>
    ///     Validates that the version list doesn't contain any invalid versions, duplicate major versions, or is unsorted.
    /// </summary>
    /// <exception cref="ArgumentException">The version list is not valid</exception>
    public void Validate()
    {
        if (Count == 0) throw new ArgumentException("Version list must contain at least one version");
        for (var i = 0; i < Count; i++)
        {
            if (this[i].Major == 0) throw new ArgumentException($"Invalid major version '{this[i].Major}'");
            if (i > 0 && this[i - 1].Major == this[i].Major)
                throw new ArgumentException($"Duplicate major version '{this[i].Major}'");
            if (i > 0 && this[i - 1].Major > this[i].Major) throw new ArgumentException("Versions are not sorted");
        }
    }

    /// <summary>
    ///     Returns the lowest version that is compatible with both version lists. If there is no compatible version,
    ///     null is returned.
    /// </summary>
    /// <param name="other">Version list to compare against</param>
    /// <returns>The highest compatible version</returns>
    public RpcVersion? IsCompatibleWith(RpcVersionList other)
    {
        RpcVersion? bestVersion = null;
        foreach (var v1 in this)
            foreach (var v2 in other)
                if (v1.Major == v2.Major && (bestVersion is null || v1.Major > bestVersion.Major))
                {
                    var v = v1.IsCompatibleWith(v2);
                    if (v is not null) bestVersion = v;
                }

        return bestVersion;
    }
}
