# Coder Desktop for Windows and Linux

Coder Desktop allows you to work on your Coder workspaces as though they're
on your local network, with no port-forwarding required. It provides seamless
access to your remote development environments through features like Coder
Connect (VPN-like connectivity) and file synchronization between local and
remote directories.

Learn more about Coder Desktop in the
[official documentation](https://coder.com/docs/user-guides/desktop).

This repo contains the C# source code for Coder Desktop. The Windows app uses
WinUI 3 (`App/`), and the Linux app uses Avalonia (`App.Avalonia/`). The VPN
service, RPC protocol, and SDK projects are shared between both platforms.
You can download the latest version from the GitHub releases.

### Contributing

#### Windows

You will need:

- Visual Studio 2022
    - .NET desktop development
    - WinUI application development
    - Windows 10 SDK (10.0.19041.0)
- Wix Toolset 5.0.2 (if building the installer)

It's also recommended to use JetBrains Rider (or VS + ReSharper) for a better
experience.

#### Linux

You will need the .NET 8 SDK. Build and test with the Linux solution filter:

```bash
dotnet restore Coder.Desktop.Linux.slnf
dotnet build Coder.Desktop.Linux.slnf
```

To run the app and service locally:

```bash
./scripts/run-linux-dev.sh --show --sudo-service
```

Linux packages (`.deb`, `.rpm`, `.tar.gz`) are built with the scripts in
`Packaging.Linux/`.

### License

The Coder Desktop for Windows source is licensed under the GNU Affero General
Public License v3.0 (AGPL-3.0).

Some vendored files in this repo are licensed separately. The license for these
files can be found in the same directory as the files.

The binary distributions of Coder Desktop for Windows have some additional
license disclaimers that can be found in
[scripts/files/License.txt](scripts/files/License.txt) or during installation.