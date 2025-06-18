# Coder Desktop for Windows

Coder Desktop allows you to work on your Coder workspaces as though they're
on your local network, with no port-forwarding required. It provides seamless
access to your remote development environments through features like Coder
Connect (VPN-like connectivity) and file synchronization between local and
remote directories.

Learn more about Coder Desktop in the [official documentation](https://coder.com/docs/user-guides/desktop).

This repo contains the C# source code for Coder Desktop for Windows. You can
download the latest version from the GitHub releases.

### Contributing

You will need:

- Visual Studio 2022
    - .NET desktop development
    - WinUI application development
    - Windows 10 SDK (10.0.19041.0)
- Wix Toolset 5.0.2 (if building the installer)

It's also recommended to use JetBrains Rider (or VS + ReSharper) for a better
experience.

### License

The Coder Desktop for Windows source is licensed under the GNU Affero General
Public License v3.0 (AGPL-3.0).

Some vendored files in this repo are licensed separately. The license for these
files can be found in the same directory as the files.

The binary distributions of Coder Desktop for Windows have some additional
license disclaimers that can be found in
[scripts/files/License.txt](scripts/files/License.txt) or during installation.