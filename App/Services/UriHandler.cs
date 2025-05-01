using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Coder.Desktop.App.Models;
using Coder.Desktop.Vpn.Proto;
using Microsoft.Extensions.Logging;


namespace Coder.Desktop.App.Services;

public interface IUriHandler : IAsyncDisposable
{
    public Task HandleUri(Uri uri, CancellationToken ct = default);
}

public class UriHandler(
    ILogger<UriHandler> logger,
    IRpcController rpcController,
    IUserNotifier userNotifier,
    IRdpConnector rdpConnector) : IUriHandler
{
    private const string OpenWorkspacePrefix = "/v0/open/ws/";

    internal class UriException(string title, string detail) : Exception
    {
        internal readonly string Title = title;
        internal readonly string Detail = detail;
    }

    public async Task HandleUri(Uri uri, CancellationToken ct = default)
    {
        try
        {
            await HandleUriThrowingErrors(uri, ct);
        }
        catch (UriException e)
        {
            await userNotifier.ShowErrorNotification(e.Title, e.Detail, ct);
        }
    }

    private async Task HandleUriThrowingErrors(Uri uri, CancellationToken ct = default)
    {
        if (uri.AbsolutePath.StartsWith(OpenWorkspacePrefix))
        {
            await HandleOpenWorkspaceApp(uri, ct);
            return;
        }

        logger.LogWarning("unhandled URI path {path}", uri.AbsolutePath);
        throw new UriException("URI handling error",
            $"URI with path {uri.AbsolutePath} is unsupported or malformed");
    }

    public async Task HandleOpenWorkspaceApp(Uri uri, CancellationToken ct = default)
    {
        const string errTitle = "Open Workspace Application Error";
        var subpath = uri.AbsolutePath[OpenWorkspacePrefix.Length..];
        var components = subpath.Split("/");
        if (components.Length != 4 || components[1] != "agent")
        {
            logger.LogWarning("unsupported open workspace app format in URI {path}", uri.AbsolutePath);
            throw new UriException(errTitle, $"Failed to open {uri.AbsolutePath} because the format is unsupported.");
        }

        var workspaceName = components[0];
        var agentName = components[2];
        var appName = components[3];

        var state = rpcController.GetState();
        if (state.VpnLifecycle != VpnLifecycle.Started)
        {
            logger.LogDebug("got URI to open workspace {workspace}, but Coder Connect is not started", workspaceName);
            throw new UriException(errTitle,
                $"Failed to open application on {workspaceName} because Coder Connect is not started.");
        }

        Workspace workspace;
        try
        {
            workspace = state.Workspaces.Single(w => w.Name == workspaceName);
        }
        catch (InvalidOperationException) // Single() throws this when nothing matches.
        {
            logger.LogDebug("got URI to open workspace {workspace}, but the workspace doesn't exist", workspaceName);
            throw new UriException(errTitle,
                $"Failed to open application on workspace {workspaceName} because it doesn't exist");
        }

        Agent agent;
        try
        {
            agent = state.Agents.Single(a => a.WorkspaceId == workspace.Id && a.Name == agentName);
        }
        catch (InvalidOperationException) // Single() throws this when nothing matches.
        {
            logger.LogDebug("got URI to open workspace/agent {workspaceName}/{agentName}, but the agent doesn't exist",
                workspaceName, agentName);
            throw new UriException(errTitle,
                $"Failed to open application on workspace {workspaceName}, agent {agentName} because it doesn't exist.");
        }

        if (appName != "rdp")
        {
            logger.LogWarning("unsupported agent application type {app}", appName);
            throw new UriException(errTitle,
                $"Failed to open agent in URI {uri.AbsolutePath} because application {appName} is unsupported");
        }

        await OpenRDP(agent.Fqdn.First(), uri.Query, ct);
    }

    public async Task OpenRDP(string domainName, string queryString, CancellationToken ct = default)
    {
        const string errTitle = "Workspace Remote Desktop Error";
        NameValueCollection query;
        try
        {
            query = HttpUtility.ParseQueryString(queryString);
        }
        catch (Exception ex)
        {
            // unfortunately, we can't safely write they query string to logs because it might contain
            // sensitive info like a password. This is also why we don't log the exception directly
            var trace = new System.Diagnostics.StackTrace(ex, false);
            logger.LogWarning("failed to parse open RDP query string: {classMethod}",
                trace?.GetFrame(0)?.GetMethod()?.ReflectedType?.FullName);
            throw new UriException(errTitle,
                "Failed to open remote desktop on a workspace because the URI was malformed");
        }

        var username = query.Get("username");
        var password = query.Get("password");
        if (!string.IsNullOrEmpty(username))
        {
            password ??= string.Empty;
            await rdpConnector.WriteCredentials(domainName, new RdpCredentials(username, password), ct);
        }

        await rdpConnector.Connect(domainName, ct: ct);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
