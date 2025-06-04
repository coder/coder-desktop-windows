using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using WixSharp;
using WixSharp.Bootstrapper;
using WixSharp.CommonTasks;
using File = WixSharp.File;
using SystemFile = System.IO.File;

namespace Coder.Desktop.Installer;

public class SharedOptions
{
    [Option('a', "arch", Required = true, HelpText = "Platform to build for (x64, arm64)")]
    public Platform Platform { get; set; }

    [Option('v', "version", Required = true, HelpText = "Product version (e.g. 1.0.0.0)")]
    public Version Version { get; set; }

    [Option('l', "license-file", Default = "License.rtf",
        HelpText = "Path to the License.rtf file to embed into the installer UI")]
    public string LicenseFile { get; set; }

    [Option("icon-file", Default = "coder.ico", HelpText = "Path to the icon file to embed into the installer")]
    public string IconFile { get; set; }

    [Option('o', "output-path", Required = true, HelpText = "Output path for the build MSI/exe")]
    public string OutputPath { get; set; }

    public void Validate()
    {
        if (Platform is not Platform.x64 and not Platform.arm64)
            throw new ArgumentException($"Invalid platform '{Platform}' specified", nameof(Platform));
        if (Version == null || Version.Major < 0 || Version.Minor < 0 || Version.Build < 0 || Version.Revision < 0)
            throw new ArgumentException($"Invalid version '{Version}' specified, must have 4 components",
                nameof(Version));
        if (!SystemFile.Exists(LicenseFile))
            throw new ArgumentException($"License file not found at '{LicenseFile}'", nameof(LicenseFile));
        if (!SystemFile.Exists(IconFile))
            throw new ArgumentException($"Icon file not found at '{IconFile}'", nameof(IconFile));
    }
}

[Verb("build-msi", HelpText = "Build the MSI package")]
public class MsiOptions : SharedOptions
{
    [Option('i', "input-dir", Required = true, HelpText = "Input directory for the files to be installed")]
    public string InputDir { get; set; }

    [Option("service-exe", Default = "CoderVpnService.exe",
        HelpText = "Sub path within --input-dir of the service executable")]
    public string ServiceExe { get; set; }

    [Option("service-name", Default = "Coder Desktop",
        HelpText =
            "The service name for the installer to create during install. This should match what the service binary expects during startup")]
    public string ServiceName { get; set; }

    [Option("app-exe", Default = "Coder Desktop.exe",
        HelpText = "Sub path within --input-dir of the desktop app executable")]
    public string AppExe { get; set; }

    [Option("vpn-dir", Default = "vpn",
        HelpText = "Sub path within --input-dir for the Coder VPN directory. Must contain wintun.dll")]
    public string VpnDir { get; set; }

    [Option("banner-bmp", Default = "banner.bmp", HelpText = "Top banner.bmp of MSI installer UI (493x58)")]
    public string BannerBmp { get; set; }

    [Option("dialog-bmp", Default = "dialog.bmp", HelpText = "Background dialog.bmp of MSI installer UI (493x312)")]
    public string DialogBmp { get; set; }

    public new void Validate()
    {
        base.Validate();

        InputDir = Path.GetFullPath(InputDir);
        if (!Directory.Exists(InputDir))
            throw new ArgumentException($"Input directory '{InputDir}' does not exist", nameof(InputDir));

        var serviceExe = Path.Combine(InputDir, ServiceExe);
        if (!SystemFile.Exists(serviceExe))
            throw new ArgumentException($"Service executable not found at '{serviceExe}'", nameof(ServiceExe));

        if (string.IsNullOrEmpty(ServiceName))
            throw new ArgumentException("Service name is required", nameof(ServiceName));

        var appExe = Path.Combine(InputDir, AppExe);
        if (!SystemFile.Exists(appExe))
            throw new ArgumentException($"App executable not found at '{appExe}'", nameof(AppExe));

        var vpnDir = Path.Combine(InputDir, VpnDir);
        if (!Directory.Exists(vpnDir))
            throw new ArgumentException($"VPN directory '{vpnDir}' does not exist", nameof(VpnDir));

        var wintunDll = Path.Combine(vpnDir, "wintun.dll");
        if (!SystemFile.Exists(wintunDll))
            throw new ArgumentException($"wintun.dll not found at '{wintunDll}'", nameof(VpnDir));

        if (!SystemFile.Exists(BannerBmp))
            throw new ArgumentException($"Banner BMP file not found at '{BannerBmp}'", nameof(BannerBmp));

        if (!SystemFile.Exists(DialogBmp))
            throw new ArgumentException($"Dialog BMP file not found at '{DialogBmp}'", nameof(DialogBmp));
    }
}

[Verb("build-bootstrapper", HelpText = "Build the bootstrapper executable")]
public class BootstrapperOptions : SharedOptions
{
    [Option("logo-png", Default = "logo.png", HelpText = "Logo.png for the bootstrapper UI (75x75)")]
    public string LogoPng { get; set; }

    [Option('m', "msi-path", Required = true, HelpText = "Path to the MSI package to embed")]
    public string MsiPath { get; set; }

    [Option('w', "windows-app-sdk-path", Required = true, HelpText = "Path to the Windows App Sdk package to embed")]
    public string WindowsAppSdkPath { get; set; }

    [Option('t', "theme-xml-path", Required = false, HelpText = "Path to the theme .xml file to use for the installer")]
    public string ThemeXmlPath { get; set; }

    public new void Validate()
    {
        base.Validate();

        if (!SystemFile.Exists(LogoPng))
            throw new ArgumentException($"Logo PNG file not found at '{LogoPng}'", nameof(LogoPng));
        if (!SystemFile.Exists(MsiPath))
            throw new ArgumentException($"MSI package not found at '{MsiPath}'", nameof(MsiPath));
        if (!SystemFile.Exists(WindowsAppSdkPath))
            throw new ArgumentException($"Windows App Sdk package not found at '{WindowsAppSdkPath}'",
                nameof(WindowsAppSdkPath));
        if (ThemeXmlPath != null && !SystemFile.Exists(ThemeXmlPath))
            throw new ArgumentException($"Theme XML file not found at '{ThemeXmlPath}'", nameof(ThemeXmlPath));
    }
}

public class Program
{
    private const string ProductName = "Coder Desktop";
    private const string Manufacturer = "Coder Technologies Inc.";
    private const string HelpUrl = "https://coder.com/docs";
    private const string RegistryKey = @"SOFTWARE\Coder Desktop";
    private const string AppConfigRegistryKey = RegistryKey + @"\App";
    private const string VpnServiceConfigRegistryKey = RegistryKey + @"\VpnService";

    private const string DotNetCheckName = "DOTNET_RUNTIME_CHECK";
    private const RollForward DotNetCheckRollForward = RollForward.minor;
    private const RuntimeType DotNetCheckRuntimeType = RuntimeType.desktop;

    private static readonly Guid MsiUpgradeCode = new("4d0a5478-9f5b-4c23-8fa1-c7d67b4242b1");
    private static readonly Guid BundleUpgradeCode = new("8a773cf1-029c-4ba8-a3f8-dda90f9e641b");

    private static readonly RegistryHive RegistryHive = RegistryHive.LocalMachine;
    private static readonly Version DotNetCheckVersion = new(8, 0, 0);

    private static readonly Dictionary<Platform, ExePackagePayload> DotNetRuntimePackagePayloads = new()
    {
        [Platform.x64] = new ExePackagePayload
        {
            Name = ".NET Desktop Runtime 8.0.13 (x64).exe",
            DownloadUrl =
                "https://download.visualstudio.microsoft.com/download/pr/fc8c9dea-8180-4dad-bf1b-5f229cf47477/c3f0536639ab40f1470b6bad5e1b95b8/windowsdesktop-runtime-8.0.13-win-x64.exe",
            Hash =
                "abeef95a520e5d22d4a8b0d369fe103c2552a5c337500582e850da3611135bb68bb479d123cee85a445310cf4db73037e6198eec40d66d4d746ef2e2e5f1450f",
            Size = 58_433_888,
        },
        [Platform.arm64] = new ExePackagePayload
        {
            Name = ".NET Desktop Runtime 8.0.13 (arm64).exe",
            DownloadUrl =
                "https://download.visualstudio.microsoft.com/download/pr/7468483d-b69c-4ff8-b900-e046f3a73e8d/fce0ba9123be8a4cc10ed1c73af09ae6/windowsdesktop-runtime-8.0.13-win-arm64.exe",
            Hash =
                "45c1fc3f5adb8551fb0ee805ad6c5046a9447da38cbe6b7e9d04fdc995d21885b8b9415ba3bc9040644d82e04aab3a88c625854efc7870ac0236d0368de90c3c",
            Size = 53_944_552,
        },
    };

    private static int Main(string[] args)
    {
        try
        {
            WixExtension.Bal.PreferredVersion = "5.0.2";
            WixExtension.NetFx.PreferredVersion = "5.0.2";
            WixExtension.UI.PreferredVersion = "5.0.2";
            WixExtension.Util.PreferredVersion = "5.0.2";

            Compiler.VerboseOutput = true;
            Compiler.OutputWriteLine = Console.Error.WriteLine;
            Compiler.WixSourceSaved += PrintFile;

            AutoElements.DisableAutoUserProfileRegistry = true;

            return Parser.Default.ParseArguments<MsiOptions, BootstrapperOptions>(args)
                .MapResult(
                    (MsiOptions opts) => BuildMsiPackage(opts),
                    (BootstrapperOptions opts) => BuildBundle(opts),
                    errs => 1);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Error: {ex}");
            return 1;
        }
    }

    private static int BuildMsiPackage(MsiOptions opts)
    {
        opts.Validate();

        var project = new Project($"{ProductName} (Core)");

        // Manually iterate over all the files in the input directory
        // recursively and create Dir and File elements for each. Also adds a
        // ServiceInstaller element for the service executable and a
        // FileShortcut element for the desktop app executable.
        var service = new ServiceInstaller
        {
            Name = opts.ServiceName,
            StartOn = SvcEvent.Install,
            StopOn = SvcEvent.InstallUninstall_Wait,
            RemoveOn = SvcEvent.Uninstall_Wait,
            DelayedAutoStart = false,
            Start = SvcStartType.auto,
            ServiceSid = ServiceSid.none,
            FirstFailureActionType = FailureActionType.restart,
            SecondFailureActionType = FailureActionType.restart,
            ThirdFailureActionType = FailureActionType.none,
            RestartServiceDelayInSeconds = 30,
            ResetPeriodInDays = 1,
            PreShutdownDelay = 1000 * 60 * 3, // default
            // This matches Tailscale's service dependencies.
            DependsOn =
            [
                new ServiceDependency("iphlpsvc"), // IP Helper
                new ServiceDependency("netprofm"), // Network List Service
                new ServiceDependency("WinHttpAutoProxySvc"), // WinHTTP Web Proxy Auto-Discovery Service
            ],
        };
        var shortcut = new FileShortcut("Coder Desktop", "%StartMenuFolder%")
        {
            IconFile = "", // not required since the app has its own icon
            WorkingDirectory = "[INSTALLFOLDER]",
            Description = "Launch Coder Desktop",
        };

        var programFiles64Folder = new Dir(new Id("ProgramFiles64Folder"));
        var installDir = new Dir(new Id("INSTALLFOLDER"), ProductName);
        RecursivelyAddDirectory(installDir, opts.InputDir, path =>
        {
            var normalizedPath = path.Replace('\\', '/');
            var normalizedServiceExe = opts.ServiceExe.Replace('\\', '/');
            var normalizedAppExe = opts.AppExe.Replace('\\', '/');
            if (normalizedPath.EndsWith(normalizedServiceExe, StringComparison.OrdinalIgnoreCase)) return [service];
            if (normalizedPath.EndsWith(normalizedAppExe, StringComparison.OrdinalIgnoreCase)) return [shortcut];
            return [];
        });

        programFiles64Folder.AddDir(installDir);
        project.AddDir(programFiles64Folder);


        project.AddRegValues(
            // Add registry values that are consumed by the manager. Note that these
            // should not be changed. See Vpn.Service/Program.cs (AddDefaultConfig) and
            // Vpn.Service/ManagerConfig.cs for more details.
            new RegValue(RegistryHive, VpnServiceConfigRegistryKey, "Manager:ServiceRpcPipeName", "Coder.Desktop.Vpn"),
            new RegValue(RegistryHive, VpnServiceConfigRegistryKey, "Manager:TunnelBinaryPath",
                $"[INSTALLFOLDER]{opts.VpnDir}\\coder-vpn.exe"),
            new RegValue(RegistryHive, VpnServiceConfigRegistryKey, "Manager:TunnelBinarySignatureSigner",
                "Coder Technologies Inc."),
            new RegValue(RegistryHive, VpnServiceConfigRegistryKey, "Manager:TunnelBinaryAllowVersionMismatch",
                "false"),
            new RegValue(RegistryHive, VpnServiceConfigRegistryKey, "Serilog:WriteTo:0:Args:path",
                @"[INSTALLFOLDER]coder-desktop-service.log"),

            // Add registry values that are consumed by the App MutagenController. See App/Services/MutagenController.cs
            new RegValue(RegistryHive, AppConfigRegistryKey, "MutagenController:MutagenExecutablePath",
                @"[INSTALLFOLDER]vpn\mutagen.exe")
        );

        // Note: most of this control panel info will not be visible as this
        // package is usually hidden in favor of the bootstrapper showing
        // instead.
        project.Description = $"Contains the required components of {ProductName} by {Manufacturer}.";
        project.ControlPanelInfo.ProductIcon = opts.IconFile;
        project.ControlPanelInfo.Comments = ProductName;
        project.ControlPanelInfo.Manufacturer = Manufacturer;
        project.ControlPanelInfo.HelpLink = HelpUrl;
        project.ControlPanelInfo.InstallLocation = "[INSTALLFOLDER]";

        project.Platform = opts.Platform;
        project.Version = opts.Version;
        project.GUID = MsiUpgradeCode;

        // Prevent downgrades and set an error message if a newer version is
        // installed.
        project.MajorUpgrade = new MajorUpgrade();
        project.MajorUpgrade.AllowDowngrades = false;
        project.MajorUpgrade.DowngradeErrorMessage = "A newer version of the product is installed.";

        // If a user launches the core installer manually, we want to show a UI
        // with the license agreement and installation directory settings.
        project.UI = WUI.WixUI_InstallDir;
        project.LicenceFile = opts.LicenseFile;
        project.WixVariables["WixUIBannerBmp"] = opts.BannerBmp;
        project.WixVariables["WixUIDialogBmp"] = opts.DialogBmp;

        // This is a property that gets injected by the bootstrapper below.
        project.Properties =
        [
            new Property("INSTALLFOLDER", "")
            {
                Secure = true,
            },
        ];

        // Check for .NET Desktop Runtime 8.0. The bootstrapper will also check
        // and install it before launching this MSI.
        //
        // For whatever reason, including the DotNetCompatibilityCheck element
        // using `project.GenericItems` does not work (I'm guessing it's related
        // to it being wrapped in a `<Fragment>` element).
        //
        // This manually adds it directly into the `<Package>` element without
        // a wrapper, which seems to work great.
        project.Include(WixExtension.NetFx);
        project.WixSourceGenerated += doc =>
            doc.FindFirst("Package").AddElement(
                WixExtension.NetFx.ToXName("DotNetCompatibilityCheck"),
                $"Property={DotNetCheckName}; RuntimeType={DotNetCheckRuntimeType}; Version={DotNetCheckVersion}; RollForward={DotNetCheckRollForward}; Platform={opts.Platform}");
        project.LaunchConditions.Add(new LaunchCondition(
            Condition.Create($"Installed OR {DotNetCheckName} = 0"),
            $"Please install .NET Desktop Runtime 8.0 (check result: [{DotNetCheckName}])"));

        // Build the MSI package.
        var outputPath = Compiler.BuildMsi(project, opts.OutputPath);
        if (string.IsNullOrEmpty(outputPath))
            throw new InvalidOperationException("MSI could not be built, output path is empty");
        if (!SystemFile.Exists(outputPath))
            throw new InvalidOperationException($"MSI was not created at '{outputPath}'");

        Console.Error.WriteLine();
        Console.Error.WriteLine($"MSI built at {outputPath}");
        return 0;
    }

    private static int BuildBundle(BootstrapperOptions opts)
    {
        opts.Validate();

        if (!DotNetRuntimePackagePayloads.TryGetValue(opts.Platform, out var dotNetRuntimePayload))
            throw new ArgumentException($"Invalid architecture '{opts.Platform}' specified", nameof(opts.Platform));

        var bundle = new Bundle(ProductName,
            new ExePackage // .NET Runtime
            {
                PerMachine = true,
                // Don't uninstall the runtime when the bundle is uninstalled.
                Permanent = true,
                // Since it's a "permanent" package, once installed we don't
                // need to keep the installer around.
                Cache = PackageCacheAction.remove,
                DetectCondition = DotNetCheckName,
                // We must not use `/quiet` so the user can accept the license
                // agreement.
                InstallArguments = "/norestart",
                // If it fails to install for whatever reason, continue
                // anyway. The MSI will fatally exit if the runtime really isn't
                // available, and the user can install it themselves.
                Vital = false,
                Payloads = [dotNetRuntimePayload],
            },
            // TODO: right now we are including the Windows App Sdk in the bundle
            //       and always install it
            //       Microsoft makes it difficult to check if it exists from a regular installer:
            //       https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/check-windows-app-sdk-versions
            //       https://github.com/microsoft/WindowsAppSDK/discussions/2437
            new ExePackage // Windows App Sdk
            {
                PerMachine = true,
                Permanent = true,
                Cache = PackageCacheAction.remove,
                // There is no license agreement for this SDK.
                InstallArguments = "--quiet",
                Vital = false,
                Payloads =
                [
                    new ExePackagePayload
                    {
                        SourceFile = opts.WindowsAppSdkPath,
                    },
                ],
            },
            new MsiPackage(opts.MsiPath)
            {
                ForcePerMachine = true,
                // Prevent the UI of the MSI installer from appearing.
                DisplayInternalUI = false,
                // Hide this subpackage from the apps list. Users can uninstall
                // this subpackage by uninstalling the bundle.
                Visible = false,
                Compressed = true,
                Vital = true,
                MsiProperties = "INSTALLFOLDER=[InstallFolder]",
            });

        bundle.Manufacturer = Manufacturer;
        bundle.HelpUrl = HelpUrl;
        bundle.IconFile = opts.IconFile;

        bundle.Platform = opts.Platform;
        bundle.Version = opts.Version;
        bundle.UpgradeCode = BundleUpgradeCode;

        bundle.Application.LicensePath = opts.LicenseFile;
        bundle.Application.LogoFile = opts.LogoPng;

        if (opts.ThemeXmlPath != null)
        {
            bundle.Application.ThemeFile = opts.ThemeXmlPath;
            bundle.Application.Payloads =
            [
                new ExePackagePayload
                {
                    Name = "icon.ico",
                    SourceFile = opts.IconFile,
                    Compressed = true,
                },
            ];
        }

        // Set the default install folder, which will eventually be passed into
        // the MSI.
        bundle.Variables =
        [
            new Variable("InstallFolder", $"[ProgramFiles64Folder]{ProductName}", false, false, VariableType.formatted),
        ];

        // Check for .NET Desktop Runtime 8.0. For similar reasons as the MSI,
        // this is manually added to the `<Bundle>` element.
        bundle.Include(WixExtension.NetFx);
        bundle.WixSourceGenerated += doc =>
            doc.FindFirst("Bundle").AddElement(
                WixExtension.NetFx.ToXName("DotNetCoreSearch"),
                $"Variable={DotNetCheckName}; RuntimeType={DotNetCheckRuntimeType}; MajorVersion={DotNetCheckVersion.Major}; Platform={opts.Platform}");

        // Build the bootstrapper executable.
        var outputPath = Compiler.Build(bundle, opts.OutputPath);
        if (string.IsNullOrEmpty(outputPath))
            throw new InvalidOperationException("Bundle could not be built, output path is empty");
        if (!SystemFile.Exists(outputPath))
            throw new InvalidOperationException($"Bundle was not created at '{outputPath}'");

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Bundle built at {outputPath}");
        return 0;
    }

    private static void PrintFile(string filePath)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Contents of {filePath}:");
        Console.Error.WriteLine(SystemFile.ReadAllText(filePath));
        Console.Error.WriteLine();
    }

    private static void RecursivelyAddDirectory(Dir parent, string directoryPath,
        Func<string, WixEntity[]> fileItemsFunc)
    {
        // Iterate over files in the directory and add them to parent.
        foreach (var file in Directory.EnumerateFiles(directoryPath))
        {
            var items = fileItemsFunc(file);
            Console.Error.WriteLine(
                $"Adding file {file} into {parent.Name} with items {string.Join(", ", items.Select(i => i.GetType().Name))}");
            parent.AddFile(new File(file, items));
        }

        // Recurse into subdirectories and add them to parent.
        foreach (var subdirectory in Directory.EnumerateDirectories(directoryPath))
        {
            var dir = new Dir(Path.GetFileName(subdirectory));
            RecursivelyAddDirectory(dir, subdirectory, fileItemsFunc);
            Console.Error.WriteLine($"Adding directory {dir.Name} into {parent.Name}");
            parent.AddDir(dir);
        }
    }
}
