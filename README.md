# PACKMAN

A Windows desktop app for packaging Win32 applications with the **PowerShell App
Deployment Toolkit (PSADT) v4** and publishing them to **Microsoft Intune**.

Packman takes an installer (MSI or EXE), wraps it in a PSADT v4 package, builds the
`.intunewin`, and uploads it to your Intune tenant via Microsoft Graph — detection
rules, group assignment, code signing and supersedence included.

---

## What it does

- **Create** a PSADT v4 package from an MSI/EXE — metadata (name, vendor, version,
  icon, MSI product code) is read from the installer automatically.
- **Configure PSADT functions** per install phase from the bundled PSADT v4 function
  catalog, without hand-editing the deploy script.
- **Edit** the generated `Invoke-AppDeployToolkit.ps1` in VS Code or PowerShell ISE.
- **Build** the `.intunewin` with Microsoft's `IntuneWinAppUtil.exe`.
- **Code-sign** package files in-process via Authenticode (optional, certificate
  from the Windows store by thumbprint).
- **Remote-test** the package on a test machine over WinRM — runs PSADT there as
  `NT AUTHORITY\SYSTEM` (as Intune does) or as the logged-on user, then discovers the
  detection rule from what actually got installed.
- **Upload** to Intune as a Win32 app, with auto-generated detection rules.
- **Assign** to Entra (Azure AD) security groups — existing groups or a new
  per-package group created on upload.
- **Upgrade** an existing package to a new version, writing an Intune supersedence
  relationship to the previous app.
- **Browse** the Win32 apps already in your tenant.

## Architecture

- **.NET 9 / WPF** desktop application (`net9.0-windows`), MVVM, no DI container.
- **Authentication** via MSAL (`Microsoft.Identity.Client`): interactive sign-in
  (using the public "Microsoft Graph Command Line Tools" client by default) or an
  app registration with a certificate.
- **Intune integration** over the Microsoft Graph **beta** endpoint
  (`deviceAppManagement/mobileApps`).
- **Remote testing** over PowerShell remoting (`System.Management.Automation`), with the
  install itself run from a scheduled task so it executes under the right identity.
- **Settings** persist to `appsettings.json` next to the executable.

```
Packman/
  Models/         data models (settings, application info, detection rules, groups)
  Services/       Intune upload/auth, PSADT generation, code signing, settings
    Intune/       Graph upload pipeline (blob upload, detection rules, assignments)
  ViewModels/     MVVM view models for each screen and wizard step
  Views/          XAML screens and wizard steps
  Helpers/        metadata/icon/MSI extraction, converters
  Themes/         light/dark themes, styles, icons
  PSADT/          bundled PSADT v4 template copied into each new package
```

## Requirements

- **Windows** (the app uses WPF and the Windows certificate store / broker).
- **.NET 9 SDK** to build, or the .NET 9 Desktop Runtime to run a published build.
- **PSADT v4 template + runtime** — a folder containing
  `Invoke-AppDeployToolkit.ps1` plus the `Invoke-AppDeployToolkit.exe` runtime and
  `PSAppDeployToolkit` module. A starter template ships in `Packman/PSADT/`
  (script only — see `Packman/PSADT/README.md`).
- **IntuneWinAppUtil.exe** — Microsoft's Win32 Content Prep Tool, used to build the
  `.intunewin`.
- **A Microsoft Intune tenant** and an account (or app registration) with the Graph
  permissions `DeviceManagementApps.ReadWrite.All`, `Group.Read.All`, `User.Read`,
  `Device.Read.All`, `GroupMember.ReadWrite.All`.

## Build & run

```powershell
git clone <repo-url>
cd Packman_v1
dotnet build Packman.sln
dotnet run --project Packman/Packman.csproj
```

Or open `Packman.sln` in Visual Studio 2022 and run.

## First steps

Open the app, go to **Settings**, and configure:

1. **Authentication** — sign in interactively, or enter a tenant/client ID and pick a
   certificate for app-registration mode. Use **Test Connection** to verify the Graph
   scopes.
2. **Network Paths** — `IntuneApplications` (where packages are written), `PSADT
   Template` (your PSADT v4 folder), and `IntuneWinAppUtil` (path to the content prep
   tool).
3. *(Optional)* **Code Signing** and **Group Assignment** defaults.

Then use **Create Package** to build and publish your first app.

See **[HOWTO.md](HOWTO.md)** for the full step-by-step walkthrough.
