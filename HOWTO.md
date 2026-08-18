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
- **Existing groups** — add named groups that are always assigned, each with its own
  intent.

Click **Save** when done.

---

## 2. Create a package (the 4-step wizard)

Click **Create Package**. The wizard runs through four steps; the two middle steps are
optional.

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

### Step 2 — Edit Script (optional)

Click **Continue** to open the generated `Invoke-AppDeployToolkit.ps1` in **VS Code**
(or **PowerShell ISE**, or the default handler) to fine-tune install logic — for
example, replacing the `<silent flags>` / `<uninstall flags>` placeholders for an EXE.
Save in your editor, then return to Packman. You can **Skip** this step.

### Step 3 — Remote Test (optional)

A placeholder for validating the package before upload. **Skip** if you don't use it.

### Step 4 — Upload

1. Review the summary (app name, version, install context, detection rule).
2. Make sure you're signed in (Settings page) and the **IntuneWinAppUtil** path is set.
3. Click **Build & Upload**.

Packman then, with a progress bar:

- signs the package files (if code signing is enabled),
- builds the `.intunewin` into the package's `Intune` folder,
- registers the Win32 app in Intune and uploads the encrypted content to Azure,
- applies the detection rule (MSI version rule when an MSI is present, otherwise a
  file-exists rule),
- assigns the groups configured in Settings,
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
| *Package version … already exists.* | Use a new version, or delete the existing version folder. |

Each upload also writes a detailed log file (see the path printed at the start of the
upload) — check it when a publish fails partway through.
