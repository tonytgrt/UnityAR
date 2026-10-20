# UnityAR

An augmented reality app for **XREAL Air 2 Ultra** glasses, built with Unity 6 and the XREAL SDK 3.1. The glasses are driven by an Android phone, which also acts as the controller.

What the app does:

- **6DoF tracking** from the glasses' own cameras.
- **Room meshing**: XREAL builds a mesh of the room as you look around, colored by surface type (floor, wall, table, and so on).
- **World origin**: tap a surface with the controller's beam to make that point Unity's world origin `(0, 0, 0)`.
- **Tagged items**: the tapped item is also tagged with a persistent XREAL anchor. The room mesh and the origin are saved with it, so in a later session the app can recognize the item and restore both without rescanning.

## What you need

| | Requirement |
| --- | --- |
| Glasses | XREAL Air 2 Ultra |
| Phone | Android 14 or later, USB-C with DisplayPort output. XREAL only tests Samsung flagships (Galaxy S24 on Android 14, S25 on Android 15) and its own Beam Pro. This project is developed on a Galaxy S22; see [S22.md](S22.md). |
| On the phone | XREAL **Control Glasses 3.1.0** (`com.xreal.glassescontrol`). The SDK loads its runtime from this app, not from the APK. |
| Computer | Unity **6000.6.0f1** with Android Build Support (including OpenJDK and Android SDK & NDK Tools). Developed on an Apple Silicon Mac. |
| Network | Phone and computer on the same Wi-Fi, for wireless debugging (the phone's only USB-C port is taken by the glasses) |

> [!WARNING]
> **Android 16 breaks XREAL** unless you change one setting. XREAL's compatibility page says: "Please do not upgrade your development devices to Android 16." Samsung phones on Android 16 / One UI 8 put a DeX desktop on the glasses, and every XREAL app then shows a black screen and crashes. The workaround is in [Prepare the phone](#3-prepare-the-phone).

## Setup

### 1. Install Unity

In Unity Hub, install **6000.6.0f1** with the **Android Build Support** module, including **OpenJDK** and **Android SDK & NDK Tools**.

### 2. Clone and open the project

```sh
git clone https://github.com/tonytgrt/UnityAR.git
```

The repository root is the Unity project. In Unity Hub, choose **Add → Add project from disk** and select the cloned folder. The first import takes a while.

**Switch to Android before doing anything else:** **File → Build Profiles → Android → Switch Platform**.

Unity remembers the build platform in `Library/`, which is not committed, so a fresh clone opens on the desktop platform. Several XREAL settings only exist while the platform is Android. If `Assets/XR/Settings/XREALSettings.asset` is saved while on another platform, those settings are silently dropped, and the phone's controller screen stays black. To check, `git diff Assets/XR/Settings/XREALSettings.asset` should show no changes, and the file should contain a `VirtualController:` line.

Everything else is committed:

- the XREAL SDK, embedded in `Packages/com.xreal.xr`
- the imported samples
- the project-specific fixes listed under [Project layout](#project-layout)

You don't need to install or import anything else.

### 3. Prepare the phone

1. **Turn on Developer options and USB debugging:** **Settings → About phone → Software information**, tap **Build number** seven times, then **Settings → Developer options → USB debugging**.

2. **Connect the phone to the computer by USB** and accept the "Allow USB debugging" prompt. Unity's copy of `adb` is at:

   ```sh
   ADB=/Applications/Unity/Hub/Editor/6000.6.0f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
   $ADB devices
   ```

3. **Install Control Glasses 3.1.0.** It isn't listed on XREAL's download page, but it is in XREAL's SDK 3.1.0 release folder:

   ```sh
   curl -LO https://public-resource.xreal.com/download/XREALSDK_Release_3.1.0.20251124/ControlGlasses-3.1.0.20251118115716-release.apk
   $ADB install -r ControlGlasses-3.1.0.20251118115716-release.apk
   ```

   The version must match the SDK. Control Glasses 1.1.0 (the SDK 3.0 version) loads, but some rendering calls fail. The older **Nebula** app isn't used by SDK 3.x.

4. **Open Control Glasses once** on the phone and accept its permission prompts. Until an app has been opened once, Android blocks the broadcasts the SDK uses to find the runtime, and XREAL apps quit with `NotFindRuntime`.

5. **Android 16 only (Samsung One UI 8):** make connected displays mirror the phone instead of starting a DeX desktop, and keep DeX off:

   ```sh
   $ADB shell settings put secure mirror_built_in_display 1
   ```

   This setting survives restarts. [S22.md](S22.md) explains why it's needed, its side effects, and how to undo it.

### 4. Build and install

1. In **File → Build Profiles**, check that **Assets/Scenes/Main.unity** is the only enabled scene.
2. Choose **Build And Run**, or **Build** and then install the APK yourself:

   ```sh
   $ADB install -r Build/Main.apk
   ```

The first build takes several minutes (IL2CPP); later builds take about a minute.

**Build And Run always ends with the error "No activity in the manifest with action MAIN and category LAUNCHER".** It is harmless: the app is installed. XREAL replaces Unity's launch activity, and Unity's auto-launch doesn't recognize the replacement. Open the app on the phone yourself, or run:

```sh
$ADB shell am start -n com.unity.template.ar_mobile/ai.nreal.activitylife.NRXRActivity
```

### 5. Run it

1. Unplug the phone from the computer and plug the glasses into the phone.
2. If Control Glasses opens and switches the glasses to 3D, let it finish.
3. Open **My project** on the phone.

The phone shows XREAL's controller: a touchpad plus APP and Home buttons. A white beam comes from the phone's position in the glasses. The status messages near the bottom of the view explain each step.

### 6. Debug over Wi-Fi

With the glasses plugged in, the phone has no free USB port, so use wireless ADB. While the phone is still connected by USB, run:

```sh
Tool/wireless-adb.sh
```

It switches ADB to TCP mode and connects over IPv4 or IPv6, and works on IPv6-only networks. After that you can unplug, and Unity's Build And Run and `adb` both work over Wi-Fi.

- **After a phone restart:** TCP mode is lost; plug in by USB and run the script again.
- **If only the Wi-Fi connection dropped:** `Tool/wireless-adb.sh <address>` reconnects without USB.

To follow the app's log:

```sh
$ADB logcat -s Unity XREAL | grep -E "\[(WorldOrigin|ItemAnchor|Status)\]|XREAL|Exception"
```

`[Status]` lines repeat the messages shown in the glasses.

## Using the app

| Action | What happens |
| --- | --- |
| Look around | The room mesh appears and grows |
| **Tap** the touchpad with the beam on the mesh | That point becomes the world origin, and the item there is tagged: a pin and a ring of segments appear around it |
| Walk around the tagged item | Ring segments turn yellow, then green, as XREAL maps it from each direction. At three green segments, the anchor, the room mesh and the origin are saved together |
| Tap somewhere else | Starts tagging there instead. Tagging the same item again replaces its earlier save |
| **APP** button, in a later session | Shows a capture frame in the middle of the view and starts looking for saved items |
| Look at a tagged item through the frame | The saved room mesh replaces live scanning, and the world origin, including its facing direction, returns to where it was saved |
| **APP** while looking, or while a saved mesh is shown | Stops looking / resumes live scanning |

Items are recognized by how they and their surroundings look in place. A tagged item moved somewhere else won't be recognized, and a rearranged desk or very different lighting can slow recognition down or prevent it.

Saved data lives in the app's storage on the phone: room meshes in `Android/data/com.unity.template.ar_mobile/files/RoomMeshes/`, and XREAL's anchor maps in the `files/` folder above it. Uninstalling the app or changing its package ID deletes or orphans them.

## Project layout

| Path | What it is |
| --- | --- |
| `Assets/Scenes/Main.unity` | The app's scene: XREAL rig, AR session, meshing, world origin, tagged items |
| `Assets/Scripts/WorldOrigin/` | `WorldOrigin` moves the XR rig so a chosen point is world `(0, 0, 0)`. `InfiniteAxes` draws the axes; its object is disabled in the scene |
| `Assets/Scripts/RoomMesh/` | `ItemAnchorController` (tagging, saving, recognition, controller input), `RoomMeshStore` and `RoomMeshFile` (the save format), `AnchorMappingGuide`, `CaptureFrame`, `StatusToast` |
| `Assets/Scripts/Interaction/BeamRaycast.cs` | Where the beam hits detected geometry |
| `Assets/XREAL/XREALVirtualController Variant.prefab` | XREAL's phone controller with its UI camera set to **Target Eye: None**. The stock prefab sizes that camera to the glasses, so the controller is drawn shrunk and off-screen on the phone |
| `Assets/XR/Settings/XREALSettings.asset` | XREAL settings, including the link to the controller variant. Keep it Android-serialized (see [Clone and open](#2-clone-and-open-the-project)) |
| `Assets/Plugins/Android/gradleTemplate.properties` | Sets `android.uniquePackageNames=false`. Two XREAL libraries share a namespace, which Unity 6.6's Android Gradle Plugin 9 otherwise rejects |
| `Packages/com.xreal.xr/` | XREAL SDK for Unity 3.1.0, embedded |
| `Assets/Samples/` | XREAL Interaction Basics and AR Features, XRI Starter Assets and Hands Interaction Demo, XR Hands HandVisualizer. `Main.unity` uses the XREAL hands rig and the meshing sample's classification script and prefabs |
| `Tool/wireless-adb.sh` | Wireless ADB setup |

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| Black screen in the glasses, black phone screen, app crashes (`SIGSEGV` in `libnr_api.so`, `NRSwapchainCreateEx`) | Android 16 / One UI 8 took over the glasses' display. Set `mirror_built_in_display` to `1` and keep DeX off; see [S22.md](S22.md) |
| App closes right away; log shows `NotFindRuntime` or `libnr_api.so not found` | The glasses aren't connected, or Control Glasses is missing, the wrong version, or was never opened. Install 3.1.0 and open it once. Right after reinstalling it, open it again before starting the app |
| Phone shows a black screen instead of the controller, but the beam works | `XREALSettings.asset` lost its Android-only fields. Switch to Android, then set **Virtual Controller** in Project Settings → XR Plug-in Management → XREAL to `Assets/XREAL/XREALVirtualController Variant.prefab` |
| Controller shown shrunk or off to one side | The stock controller prefab is in use instead of the variant (see the row above) |
| Build fails: "XREAL command line tool fail to build an image database", `exitCode:137` | macOS quarantined XREAL's image tool, which happens if the repo was downloaded as a ZIP through a browser. Run `xattr -d com.apple.quarantine "Packages/com.xreal.xr/Tools~/MacOS/trackableImageTools"` |
| Build fails: "Namespace 'nrsdk.pack' is used in multiple modules" | `Assets/Plugins/Android/gradleTemplate.properties` is missing |
| Build And Run ends with "No activity … MAIN … LAUNCHER" | Harmless; open the app on the phone |
| Tapping does nothing | The beam must be on the room mesh. Ignored taps log `Tap ignored: beam is not on a detected surface` |
| Phone keeps restarting to install updates | See [S22.md](S22.md) for how automatic updates were blocked |

## Status

- Verified on the S22:
  - 6DoF tracking, meshing, setting the origin by tapping, and the phone controller.
  - The SDK on Android 16, using the workaround above.
  - Mapping and saving a tagged item, using an earlier version of the flow that was started with APP rather than a tap.
- Not yet verified on a device: the current flow (tap to tag, saving the origin with the item, recognizing it through the capture frame, restoring the mesh and origin). It was installed just before the phone's Android 16 update. If recognition never triggers, the `[ItemAnchor]` log lines record each saved anchor's tracking-state changes, which shows how XREAL reports a recognized anchor.
- The package ID is still the Unity template's `com.unity.template.ar_mobile`. Changing it moves the app's data folder, so do it before saving items you want to keep.
