using Coder.Desktop.App.Models;
using Coder.Desktop.MutagenSdk.Proto.Url;
using MutagenProtocol = Coder.Desktop.MutagenSdk.Proto.Url.Protocol;

namespace Coder.Desktop.App.Services;

public class CreateSyncSessionRequest
{
    public required Endpoint Alpha { get; init; }
    public required Endpoint Beta { get; init; }

    public class Endpoint
    {
        public enum ProtocolKind
        {
            Local,
            Ssh,
        }

        public required ProtocolKind Protocol { get; init; }
        public string User { get; init; } = "";
        public string Host { get; init; } = "";
        public uint Port { get; init; } = 0;
        public string Path { get; init; } = "";

        public URL MutagenUrl
        {
            get
            {
                var protocol = Protocol switch
                {
                    ProtocolKind.Local => MutagenProtocol.Local,
                    ProtocolKind.Ssh => MutagenProtocol.Ssh,
                    _ => throw new ArgumentException($"Invalid protocol '{Protocol}'", nameof(Protocol)),
                };

                return new URL
                {
                    Kind = Kind.Synchronization,
                    Protocol = protocol,
                    User = User,
                    Host = Host,
                    Port = Port,
                    Path = Path,
                };
            }
        }
    }
}

public interface ISyncSessionController : IAsyncDisposable
{
    event EventHandler<SyncSessionControllerStateModel> StateChanged;
    SyncSessionControllerStateModel GetState();
    Task<SyncSessionControllerStateModel> RefreshState(CancellationToken ct = default);
    Task<SyncSessionModel> CreateSyncSession(CreateSyncSessionRequest req, Action<string> progressCallback,
        CancellationToken ct = default);
    Task<SyncSessionModel> PauseSyncSession(string identifier, CancellationToken ct = default);
    Task<SyncSessionModel> ResumeSyncSession(string identifier, CancellationToken ct = default);
    Task TerminateSyncSession(string identifier, CancellationToken ct = default);
}
