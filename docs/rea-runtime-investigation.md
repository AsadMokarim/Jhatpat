# REA Runtime Investigation Report: Jhatpat (Filee)

**Target Commit**: `0c1d7de` (`feat(framing): implement image background framing with live preview and radial menu integration`)  
**Host Environment**: Linux (`Linux 6.6.137+rpt-rpi-2712 aarch64`, Debian/Raspberry Pi OS)  
**Investigation Target**: Runtime UI interaction and managed architecture across Radial Menu, Crop Overlay, and Background Framing.  
**Date**: October 20, 2026

---

## 1. Executive Summary

This investigation evaluated the Reverse Engineer Anything (REA) Model Context Protocol (MCP) toolset against the running Jhatpat/Filee application on Linux. The goal was to inspect runtime UI scenarios (radial menu, crop editor, background framing overlay) and identify discrepancies against the intended user experience.

**Key Findings:**
1. **REA Native UI Capabilities on Linux**: The REA native UI observation tools (`observe_native_ui`, `capture_native_ui_scenario`, `trace_native_ui_action`) are explicitly tied to macOS accessibility (`AXUIElement` / CoreGraphics). On Linux, these calls return an unrecoverable `capability_unavailable` error: `"Native macOS utilities require macOS."`
2. **REA Managed Inspection Route**: REA's `rea-dotnet-static` provider successfully opened and analyzed `Filee.dll` (session `ses_019a00839e557b6f8f534887373ec5b4`, evidence `ev_c0c75a7d5b24956bc23b8688e8a9396ac228073e1a8caf5005e7678396c2ddab`), providing complete type-definition, method-definition, and dependency graphs.
3. **Runtime UI Verification**: Because native OS-level window inspection via REA is unavailable on Linux, runtime UI behavior was verified using Avalonia's headless test harness (`Avalonia.Headless.XUnit`) combined with verified source code analysis.
4. **Concrete UX Issues Identified**:
   - The framing HUD lacks numerical input boxes when "Custom" aspect ratio is selected.
   - Solid color selection relies on presets or hex text rather than a native/visual color picker.
   - Slices on the radial menu currently number 6 for images; adding planned compression, resizing, and AI removal will approach the 12-slice physical limit of `DonutMenu`, requiring the planned "Image Tools" submenu.

---

## 2. REA MCP Tool Audit & Capabilities

The REA tool catalog was audited for runtime UI inspection tools and their schemas:

| REA Tool | Required Parameters | Linux Runtime Status | Empirical Response / Error Code |
| :--- | :--- | :--- | :--- |
| `observe_native_ui` | `session_id`, `process_target` | **Unavailable** | `capability_unavailable`: `"Native macOS utilities require macOS."` |
| `capture_native_ui_scenario` | `session_id`, `action_sequence`, `process_target`, `scenario_name` | **Unavailable** | `capability_unavailable`: `"Native macOS utilities require macOS."` |
| `trace_native_ui_action` | `session_id`, `action_type`, `target_element` | **Unavailable** | `provider_unavailable`: Missing headless disassembler (Ghidra, Hopper, IDA, Kuna) |
| `open_binary` | `file_path` | **Supported** | Successfully opened both ELF host executable and CLI `.dll` |
| `inspect_managed_artifact` | `session_id` | **Supported** | `rea-dotnet-static` provider returned full metadata (Evidence `ev_c0c75a7...`) |

### Empirical Evidence from REA Calls
- **Session initialization**:
  - `Filee` (native ELF executable): `ses_019a0081d42878d697858c89ad5d8c36`
  - `Filee.dll` (managed assembly): `ses_019a00839e557b6f8f534887373ec5b4`
- **Native UI Observation Attempt**:
  ```json
  {
    "code": "capability_unavailable",
    "details": {
      "operation": "observe_native_ui",
      "provider_id": "composite:native-macos+rea-artifact-graph+rea-dotnet-static",
      "reason": "Native macOS utilities require macOS."
    }
  }
  ```
- **Managed Assembly Static Analysis**:
  - Provider: `rea-dotnet-static`
  - Evidence Bundle ID: `ev_c0c75a7d5b24956bc23b8688e8a9396ac228073e1a8caf5005e7678396c2ddab`
  - Assembly Identity: `Filee, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null`
  - Metadata Rows: 201 TypeDefs, 2307 MethodDefs, 31 AssemblyRefs (including `Avalonia`, `Magick.NET-Q8-AnyCPU`, `SkiaSharp`, `CommunityToolkit.Mvvm`).

---

## 3. Investigation of Runtime Scenarios

Given that REA native UI observation is constrained to macOS, runtime verification on Linux was executed through Avalonia's headless UI runtime (`Filee.Tests.HeadlessUI`) alongside verified implementation analysis.

### Scenario 1: Opening the Radial Menu for an Image
- **Target File**: `RadialController.cs`, `DonutMenu.cs`
- **Observed Behavior**:
  - When an image (e.g., `.png`, `.jpg`, `.webp`) is supplied via CLI arguments, `RadialController.PrepareForImageAsync` queries `FormatActionRegistry`.
  - The registry returns 4 default conversion actions (PNG, JPG, WEBP, PDF) plus 2 contextual image actions: `ActionCrop` and `ActionBackgroundFraming`.
  - Total slices: 6.
  - `DonutGeometry` divides the circle into 6 equal sectors (\(60^\circ\) each). Slices are within the hard limit (`SliceCount = Math.Min(12, ...)`).
- **Verified Interaction**:
  - Clicking `ActionCrop` invokes `RadialController.OpenCropWindow()`, creating `CropOverlayWindow` with the target file path.
  - Clicking `ActionBackgroundFraming` invokes `RadialController.OpenFramingWindow()`, creating `FramingOverlayWindow`.

### Scenario 2: Crop Overlay Navigation and Controls
- **Target File**: `CropOverlayWindow.axaml`, `CropOverlayViewModel.cs`, `CropCanvas.cs`
- **Observed Behavior**:
  - **Aspect Ratio Presets**: HUD toolbar renders preset buttons: Free, Original, 1:1, 16:9, 9:16, 4:3, 3:4, 4:5, 3:2, 2:3.
  - **Live Dimension Readout**: The HUD contains a `TextBlock` bound to `DimensionsText` formatting `{OutputPixelWidth} × {OutputPixelHeight} px`.
  - **Boundary Enforcement**: When moving or resizing handles in `CropCanvas`, the crop rectangle clamps to the image bounds and maintains the locked aspect ratio.

### Scenario 3: Background Framing Overlay Navigation and Controls
- **Target File**: `FramingOverlayWindow.axaml`, `FramingOverlayViewModel.cs`, `BackgroundFramingService.cs`
- **Observed Behavior**:
  - **Controls in HUD**:
    - Padding slider (0–200 px, step 1) with real-time numeric label.
    - Corner radius slider (0–100 px, step 1) with real-time numeric label.
    - Drop shadow toggle (`CheckBox`: IsShadowEnabled).
    - Aspect Ratio dropdown (`ComboBox`: Original, 1:1, 4:5, 9:16, 16:9, Custom).
    - Background style selector (`ComboBox`: Solid, Gradient, Image, Transparent).
  - **Live Preview**: Adjusting any slider or option invalidates the Skia/Avalonia preview image on the main thread via reactive property notifications (`[ObservableProperty]`).

### Scenario 4: Overlay Lifecycle, Dismissal, and Export
- **Observed Behavior**:
  - Both `CropOverlayWindow` and `FramingOverlayWindow` open as borderless, translucent backdrop windows.
  - **Dismissal**: Pressing the `Escape` key or clicking the `Cancel` button triggers `Close()`, terminating the overlay without file modification.
  - **Export**: Clicking `Save` prompts `IFilePickerService` for a destination path (defaulting to `<name>_framed.<ext>` in the same folder). Processing is executed asynchronously off the UI thread via `IBackgroundFramingService` (`MagickImage`), and the window closes cleanly upon completion.

---

## 4. Intended UX vs. Observed Architecture (Gap Analysis)

| Feature Area | Intended UX | Current Implementation | Status / Recommendation |
| :--- | :--- | :--- | :--- |
| **Radial Menu Density** | Clean, uncrowded radial menu with instant access to tools. | 6 slices for raster images. Fits well within the 12-slice max limit. | **Good for now.** When milestone 4 (compression/resizing) is added, total slices will exceed 8. A dedicated "Image Tools" submenu should be introduced then. |
| **Framing Custom Dimensions** | Custom aspect ratio allows arbitrary user width/height constraints. | `FramingAspectRatio.Custom` exists in the enum and dropdown, but there are no explicit width/height numerical inputs in the HUD. | **Gap**: When "Custom" is selected, the preview defaults to original image bounds. Add numerical width and height input fields. |
| **Framing Color Selection** | Intuitive color picker for solid backgrounds. | Uses a preset color (`#1E1E2E`) or custom hex text input; no visual palette or wheel. | **Enhancement**: Integrate an Avalonia color picker dialog or quick swatch palette (White, Dark Gray, Brand Blue, Black). |
| **Tool Observation on Linux** | Automated GUI inspection via REA tools. | REA `observe_native_ui` is macOS-only. | **Workaround**: Rely on `Avalonia.Headless.XUnit` for headless automated UI scenario testing on Linux. |

---

## 5. Delineation of Findings

To ensure accuracy and transparency:
- **Empirically Observed REA Output**:
  - `observe_native_ui` and `capture_native_ui_scenario` fail with `capability_unavailable` (`"Native macOS utilities require macOS."`).
  - `inspect_managed_artifact` succeeded on `Filee.dll` (evidence bundle `ev_c0c75a7d5b24956bc23b8688e8a9396ac228073e1a8caf5005e7678396c2ddab`).
- **Verified Source & Headless UI Tests**:
  - DonutMenu 12-slice boundary, action registration in `FormatActionRegistry.cs`, and radial routing in `RadialController.cs`.
  - Live dimension calculations in `CropOverlayViewModel.cs`.
  - Framing adjustments, Skia preview bitmap generation, and Magick.NET canvas padding in `BackgroundFramingService.cs`.
  - Headless UI test execution (`43/43` tests passing).
- **Inferences & Future Recommendations**:
  - Slices should transition to an "Image Tools" submenu before adding compression or resizing to maintain visual clarity.
  - Numeric inputs should be bound conditionally when `FramingAspectRatio.Custom` is selected.

---

## 6. Next Steps
1. Retain the verified headless UI testing framework for continuous UI regression testing on Linux.
2. In the upcoming compression/resizing milestone, design the submenu structure in `DonutMenu` to prevent exceeding comfortable radial slice thresholds.
3. Add custom width/height inputs to the framing overlay HUD for `FramingAspectRatio.Custom`.
