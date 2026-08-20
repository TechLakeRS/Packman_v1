# PACKMAN — How-To Guide

This guide walks through configuring Packman and packaging an application end to end,
from an installer file to a published Intune Win32 app.

> **Prerequisites:** see [README.md](README.md). You need Windows, a PSADT v4 template
> folder, `IntuneWinAppUtil.exe`, and an Intune account with the required Graph
> permissions.

---

## 1. One-time setup (Settings page)

Open Packman and click **Settings** in the sidebar.

### Authentication

Pick how Packman signs in to Microsoft Graph:

- **Interactive** (default) — click **Sign In** and complete the Microsoft prompt.
  No app registration needed; Packman uses the public *Microsoft Graph Command Line
  Tools* client. Optionally set a **Tenant ID** to restrict sign-in to one tenant.
- **App registration** — enter your **Tenant ID** and **Client ID**, then select the
  authentication **certificate** (from the Windows store, or by thumbprint). The app
  must have application permissions for `DeviceManagementApps.ReadWrite.All`,
  `Group.Read.All`, `Device.Read.All` and `GroupMember.ReadWrite.All`.

Click **Test Connection** to confirm each required Graph scope is granted. The footer
status dot turns green and reads *Connected to Microsoft Intune* once signed in.

### Network Paths

| Setting | What it is |
|---|---|
| **Intune Applications** | Root folder where generated packages are written (e.g. a network share). |
| **PSADT Template** | Your PSADT v4 folder containing `Invoke-AppDeployToolkit.ps1` and the runtime. |
| **IntuneWinAppUtil** | Full path to `IntuneWinAppUtil.exe` (Microsoft Win32 Content Prep Tool). |

Generation and upload are blocked until these are set.

### Code Signing (optional)

Enable to Authenticode-sign package files before upload. Choose the signing
certificate from the Windows store (by thumbprint) and, if needed, a timestamp server
(defaults to DigiCert). Signing runs in-process — no temporary PFX is written to disk.

### Group Assignment (optional)

Set defaults applied to every upload from the **Create Package** wizard:

- **Create a group per package** — auto-create a new Entra security group named from a
  template (tokens `%vendor%`, `%appName%`, `%appVersion%`) with a chosen intent
  (Required / Available / Uninstall). A live preview shows how the name resolves.
- **Existing groups** — add named groups, each with its own intent. These pre-fill the
  assignment picker on the wizard's Upload step, where they can be edited per package.

Click **Save** when done.

---

## 2. Create a package (the wizard)

Click **Create Package**. The wizard runs through three steps — Generate, Upload and
Review — with Edit Script and Remote Test available as optional side trips from
Generate.

### Step 1 — Generate

1. Select the source installer (**MSI** or **EXE**). Packman detects the type and
   auto-fills **App Name**, **Manufacturer**, **Version** and the icon from the file's
   metadata (and MSI product code for MSIs).
2. Adjust the fields if needed and choose **System** or **User** install context.
3. *(Optional)* **Configure PSADT** to add functions to specific phases
   (Pre-Installation, Installation, etc.) from the bundled PSADT v4 catalog.
4. Click **Generate Package**.

Packman copies the PSADT template into
`<IntuneApplications>/<Vendor>_<AppName>/<Version>/`, copies your installer into
`Application/Files`, and edits `Invoke-AppDeployToolkit.ps1` with the metadata,
install/uninstall commands, and any configured functions.

> A version that already exists won't be overwritten — bump the version or remove the
> old folder.

### Edit Script (optional)

Click **Continue** to open the generated `Invoke-AppDeployToolkit.ps1` in **VS Code**
(or **PowerShell ISE**, or the default handler) to fine-tune install logic — for
example, replacing the `<silent flags>` / `<uninstall flags>` placeholders for an EXE.
Save in your editor, then return to Packman. You can **Skip** this step.

### Remote Test (optional)

Stages the generated package on a test machine and runs it there, so you see the real
install before anything reaches Intune. **Skip** if you don't use it.

1. Enter the **Computer Name** of the test machine and click **CHECK** to confirm it
   responds. Machines you've used before are kept in the dropdown.
2. Pick the **Run Context**:
   - **SYSTEM** (default) — runs as `NT AUTHORITY\SYSTEM`, the same identity the Intune
     Management Extension uses. This is the one that matches production: SYSTEM has a
     different `%TEMP%` and HKCU than your admin account, and reaches network shares as
     the *machine* account, so a package that works under your own login can still fail
     here.
   - **USER** — runs in the logged-on user's interactive session, so their profile and
     HKCU apply and PSADT's dialogs are visible. Requires somebody to be logged on.
3. Click **RUN INSTALL**. Packman copies the package to `C:\Temp\Packman\...` on the
   target over the admin share, registers a one-shot scheduled task under the chosen
   identity, and streams the PSADT output into the console as it runs. **RUN UNINSTALL**
   does the same with `-DeploymentType Uninstall`.
4. After a successful install, Packman searches the target for the executable that was
   installed and proposes a **detection rule** built from its real path and version.
   Click **USE FOR PUBLISH** to carry that rule into Step 4 instead of the one guessed
   from the package. **DISCOVER DETECTION RULE** re-runs that search on its own.

Exit codes `0`, `3010` and `1641` count as success (the latter two mean *reboot
required*).

> **Requirements:** the test machine needs WinRM enabled (`Enable-PSRemoting`) and
> reachable through the firewall, and your account needs administrative rights on it
> (the package copy goes over the `C$` admin share).

### Step 2 — Upload

Nothing is sent to Intune from this step; it's where you decide how the app lands.

1. **Name in Intune** — pre-filled from the display-name template in Settings.
2. **Detection method** — *Auto (from package)* or a File / Registry / MSI rule you fill in.
3. **PSADT deploy mode** — how `Invoke-AppDeployToolkit.exe` runs on the device:

   | Mode | Behaviour |
   |---|---|
   | **Auto** (default) | PSADT decides: dialogs when a user is logged on, silent otherwise. |
   | **Interactive** | Always shows the PSADT dialogs. |
   | **NonInteractive** | Shows dialogs but never waits for the user. |
   | **Silent** | No dialogs at all. |

   Anything other than *Auto* appends `-DeployMode <mode>` to the Intune install and
   uninstall command lines; the resulting command is previewed under the picker. A
   command that already sets `-DeployMode` in Settings is left as written.
4. *(Optional)* **Requirements & return codes** — pre-filled from Settings ▸ Intune Defaults.
5. **Assignment** (right-hand column) — pre-filled with the groups from
   Settings ▸ Group Assignment. Pick an assignment type (Required / Available /
   Uninstall), search Entra for a group and click it to add. Each chip keeps its own
   type, so one package can mix them. Remove any you don't want for this package.
   A default group that no longer exists in Entra is shown but skipped on upload.

### Step 3 — Review

Everything you chose, read-only, in one place: package metadata, deploy mode, the
install/uninstall command lines, detection rule, minimum OS and the assignment chips.

1. Check the summary and make sure you're signed in (Settings page).
2. Make sure the **IntuneWinAppUtil** path is set.
3. Click **Build & Upload**.

Packman then, with a progress bar:

- signs the package files (if code signing is enabled),
- builds the `.intunewin` into the package's `Intune` folder,
- registers the Win32 app in Intune and uploads the encrypted content to Azure,
- applies the detection rule you chose,
- assigns the groups listed on the Review step (plus a per-package group, if that
  option is enabled in Settings),
- writes a local marker file recording the new Intune **App ID**.

On success the status shows **Uploaded to Intune · App ID …**.

---

## 3. Upload an already-built package (standalone)

Use **Upload to Intune** in the sidebar when you already have a built PSADT package
folder and just want to publish it:

1. Select the package folder (the one containing `Application/`).
2. Packman validates it and reads the metadata.
3. Review or edit the **detection rules** (add File / Registry / MSI rules as needed).
4. Search and add **Entra groups**, each with an intent (Required / Available /
   Uninstall).
5. Click **Upload**. A four-step overlay tracks validation, upload, app creation and
   group assignment.

---

## 4. Upgrade an existing package

From **Create Package**, switch to the upgrade flow to roll out a new version:

1. Select the **existing** package folder — Packman reads its vendor/name/version and
   install context.
2. Select the **new** source installer; the new version is auto-filled from its
   metadata.
3. Click **Upgrade Package**.

Packman builds a new version folder from the new source and carries the metadata into
the wizard. When you upload, it writes an Intune **supersedence** relationship marking
the previous app as superseded by the new one.

---

## 5. Browse tenant apps

**Applications** lists the Win32 apps already in your tenant (50 per page, with search
and category filters). Open a row to see its detail. Requires being signed in.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| *Configure IntuneApplications and PSADTTemplate paths in Settings first.* | Set both **Network Paths** on the Settings page. |
| *Set the IntuneWinAppUtil path…* | Point **IntuneWinAppUtil** at `IntuneWinAppUtil.exe`. |
| *PSADT template not found.* | The template path must contain `Invoke-AppDeployToolkit.ps1`. |
| *No `.intunewin` file found after conversion.* | The template needs the full PSADT v4 **runtime** (`Invoke-AppDeployToolkit.exe` + `PSAppDeployToolkit` module), not just the script. See `Packman/PSADT/README.md`. |
| *Sign in to Intune on the Settings page first.* | Sign in (and **Test Connection**) before uploading. |
| Remote test: *WinRM connection failed* | Run `Enable-PSRemoting -Force` on the test machine and allow WinRM through its firewall. |
| Remote test: *&lt;host&gt; is not reachable* | The target didn't answer a ping — check the name and that the machine is powered on. |
| Remote test: *No user is logged on…* | A **USER** context run needs somebody signed in to the target. Use **SYSTEM**, or log on first. |
| Remote test: *No matching application files found* | Detection discovery couldn't find the installed executable — set the detection rule by hand on the publish step. |
| *Package version … already exists.* | Use a new version, or delete the existing version folder. |

Each upload also writes a detailed log file (see the path printed at the start of the
upload) — check it when a publish fails partway through.
