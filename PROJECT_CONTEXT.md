# SKD750Control - Project Context & Current State

## Project Overview
WinForms application for Nikon D750 camera control with Live View (24fps) and click-to-focus AF point selection.

**Target Framework:** .NET Framework 4.8.1  
**Platform:** x64  
**SDK:** Nikon MAID SDK via nikoncswrapper.dll

---

## Current Issue: Building with COM Interop

### Problem
The project includes Windows Portable Devices (WPD) COM references for MTP command 0x9205 (ChangeAfArea). Standard `dotnet build` fails because .NET Core MSBuild doesn't support `ResolveComReference`.

### Solution Required
Build using Visual Studio's MSBuild with Windows SDK tools (tlbimp/AxImp).

### Build Commands
```powershell
# In Developer PowerShell for VS 2026:
cd "c:\Main Hub\VS Code Projects\SKD750Control"
msbuild SKD750Control.csproj /v:m
```

Or use Visual Studio IDE:
1. File → Open → Project/Solution → `SKD750Control.sln`
2. Build → Build Solution (Ctrl+Shift+B)

### Required VS Workloads
- .NET desktop development
- Desktop development with C++

### Required Components
- Windows 11 SDK (10.0.22621.x)
- MSBuild
- MSVC v14.x build tools (x64/x86)
- .NET Framework 4.8.1 targeting pack

---

## Architecture & Key Files

### NikonWpd.cs (NEW - Primary AF Implementation)
**Purpose:** Send MTP/PTP command 0x9205 (ChangeAfArea) directly to D750 via Windows Portable Devices API.

**Why:** Nikon MAID SDK's `ContrastAFArea` capability reports `CanSet=False` on D750. The MTP command bypasses this limitation (proven working in DigiCamControl2).

**Key Method:**
```csharp
public static bool TrySetAfArea(int x, int y)
```
- Enumerates portable devices, finds Nikon D750
- Opens device via `IPortableDevice`
- Sends PTP command 0x9205 with x,y parameters
- Returns true on success, logs HRESULT on failure

**COM References Required:**
- `PortableDeviceApiLib` (GUID: {1F001332-1A57-4934-BE31-AFFC99F4EE0A})
- `PortableDeviceTypesLib` (GUID: {2B00BA2F-E750-4BEB-9235-97142EDE1D3E})

### MainForm.cs
**Click-to-Focus Flow (pictureBox_MouseClick):**
1. Convert click coords to image space (handles letterbox/pillarbox)
2. Apply adjustable transform: `tx = round(imgX * afScaleX) + afOffsetX`
3. Apply flipY if enabled: `ty = imgHeight - ty - 1`
4. Clamp to image bounds
5. **Primary path:** Call `NikonWpd.TrySetAfArea(tx, ty)`
6. **Fallback path:** If WPD fails AND `ContrastAFArea.CanSet==true`, use MAID SDK `SetPoint`
7. Trigger ContrastAF (Start) with busy-retry logic
8. Update overlay and log

**User Controls:**
- Scale X/Y (NumericUpDown, default 1.0, range 0.5-2.0)
- Offset X/Y (NumericUpDown, default 0, range ±500)
- Flip Y (CheckBox, default false)
- Calibrate AF button (runs 9-point test, logs rawSet vs readBack)

**Logging:** All AF events logged via `AppLogger.Info()` to `bin\Debug\app.log`

### AppLogger.cs
Thread-safe file logger. Format: `yyyy-MM-dd HH:mm:ss.fff [LEVEL] message`

### NikonMtp.cs (DEPRECATED STUB)
Placeholder, always returns false. Replaced by `NikonWpd.cs`.

---

## Known Issues & Discoveries

### 1. MAID SDK ContrastAFArea Disabled on D750
**Evidence from logs:**
```
PointCap: ID=0000824A [Contrast AF Area] CanGet=False CanSet=False
```
The D750 firmware disables this capability. The MAID SDK cannot set Live View AF points on this camera model.

### 2. DigiCamControl2 Solution
DigiCamControl uses MTP command `CONST_CMD_ChangeAfArea = 0x9205` with `ExecuteWithNoData((uint)x, (uint)y)`. This works reliably.

### 3. nikoncswrapper Limitation
The C# wrapper doesn't expose `ExecuteWithNoData` or custom MTP operations. We must use WPD COM interop directly.

### 4. Coordinate Space Unknown
Without read-back capability, we don't know the camera's expected coordinate range/origin. The adjustable Scale/Offset/FlipY controls allow runtime calibration.

---

## Testing Instructions

### After Successful Build:

1. **Run the app:** `bin\Debug\SKD750Control.exe`

2. **Connect D750 and enable Live View**

3. **Test click-to-focus:**
   - Click various points on the Live View image
   - Observe where the camera focuses (green rectangle in viewfinder)
   - If focus is off-target, adjust Scale X/Y or Offset X/Y controls

4. **Check the log:** `bin\Debug\app.log`
   - Look for: `WPD: Found Nikon device: ...`
   - Look for: `WPD: ChangeAfArea command sent successfully: (x,y)`
   - If failed: Note the HRESULT error code

5. **Optional calibration:**
   - Click "Calibrate AF" button
   - Waits for 9-point test (corners, edges, center)
   - Check log for `CalibTest rawSet=(...) readBack=(...)` entries

### Expected Log Entries (Success):
```
ClickToFocus raw=(320,240) tx=(320,240) flipY=False scale=(1.00,1.00) offset=(0,0)
WPD: Found Nikon device: Nikon D750
WPD: ChangeAfArea command sent successfully: (320,240)
ContrastAF trigger sent
```

### Expected Log Entries (WPD Failure):
```
WPD: No portable devices found
```
or
```
WPD: ChangeAfArea command failed with HRESULT=0x80070057
```

### Fallback Behavior:
If WPD fails and `ContrastAFArea.CanSet==true`, logs:
```
ContrastAFArea SetPoint succeeded
ContrastAFArea read-back=(x,y)
```

If capability disabled:
```
ContrastAFArea SetPoint skipped (capability CanSet=False)
```

---

## Next Steps After Build Success

1. **Test WPD AF command:** Confirm log shows "WPD: ChangeAfArea command sent successfully"

2. **Verify AF accuracy:** Does the camera focus where you click? If not:
   - Adjust Scale X/Y (try 0.8-1.2 range first)
   - Adjust Offset X/Y (try ±50 range first)
   - Toggle Flip Y if Y-axis is inverted

3. **Clean house (code cleanup):**
   - Remove unused MAID fallback code if WPD works reliably
   - Remove calibration button/controls if not needed
   - Remove unused capability detection fields
   - Fix compiler warnings (unused fields)

4. **Portrait orientation:** Add rotation transform based on `CameraInclination` capability

---

## Build Troubleshooting

### Error: "ResolveComReference is not supported"
**Cause:** Using .NET Core MSBuild (`dotnet build`)  
**Fix:** Use Visual Studio's MSBuild or Developer PowerShell

### Error: "AxImp.exe was not found"
**Cause:** Windows SDK not installed  
**Fix:** VS Installer → Modify → Desktop development with C++ workload

### Error: COM type library not found
**Cause:** COM references not resolved  
**Fix:** In VS, Project → Add Reference → COM → "Portable Device API" and "Portable Device Types"

### msbuild command does nothing
**Cause:** Developer shell not initialized  
**Fix:** Run `& "C:\Program Files\Microsoft Visual Studio\2026\Community\Common7\Tools\Launch-VsDevShell.ps1"` or use VS IDE

---

## References

- **DigiCamControl2:** https://github.com/dukus/digiCamControl (MTP 0x9205 implementation reference)
- **nikoncswrapper:** https://sourceforge.net/projects/nikoncswrapper/
- **Nikon MAID SDK:** Maid3.h, Maid3d1.h in `lib\NikonSDK\`
- **WPD API:** Windows Portable Devices COM API (PortableDeviceApi.dll)

---

## Key Technical Decisions

1. **Why WPD over MAID SDK?**
   - D750 firmware disables `ContrastAFArea` capability
   - MTP 0x9205 is the intended protocol for Live View AF (per DigiCamControl)
   - Direct hardware command avoids SDK limitations

2. **Why COM interop?**
   - WPD is a Windows COM API
   - No managed .NET wrapper available for PTP/MTP operations
   - DigiCamControl uses the same approach

3. **Why adjustable transform controls?**
   - Camera's coordinate space unknown (no read-back capability)
   - Different firmware versions may expect different ranges
   - Runtime calibration more flexible than hardcoded values

4. **Why keep MAID fallback?**
   - Future camera models may support `ContrastAFArea`
   - Graceful degradation if WPD fails
   - Gated by `CanSet` check to avoid wasted calls

---

## Current Todo List

- [x] Add WPD COM references to .csproj
- [x] Implement `NikonWpd.TrySetAfArea()` with MTP 0x9205
- [x] Wire MainForm to call WPD path first
- [x] Gate MAID fallback with `CanSet` check
- [ ] **Build and test** (blocked on VS environment setup)
- [ ] Verify WPD logs and AF accuracy
- [ ] Clean up unused code and warnings
- [ ] Add portrait orientation support

---

*Generated: 2025-12-01*  
*For use with Visual Studio 2026 Community*
