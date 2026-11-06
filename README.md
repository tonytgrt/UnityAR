# UnityAR

An augmented reality app for **XREAL Air 2 Ultra** glasses, built with Unity 6 and the XREAL SDK 3.1. The glasses are driven by an Android phone, which also acts as the controller.

What the app does:

- **6DoF tracking** from the glasses' own cameras.
- **Room meshing**: XREAL builds a mesh of the room as you look around, colored by surface type (floor, wall, table, and so on).
- **Hand tracking**: pinch in empty space to place a small cube, and pinch a cube to pick it up and move it.
- **Scan images**: look at a pre-registered image (**Scan A**) to make it Unity's world origin `(0, 0, 0)` and save the room to it: the room mesh, the cubes and the origin. In a later session, look at the image again to get all of them back without rescanning.

Buttons added to the phone's controller screen save and load the room.

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

The phone shows XREAL's controller: four project buttons at the top, then XREAL's APP button, touchpad and Home button. The phone is only used for the project buttons; APP and the touchpad do nothing. Pointing is done with your hands. When a hand is in view of the glasses, its joints are drawn as dots, with a ring between the thumb and index fingertips. The status messages near the bottom of the view explain each step.

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
$ADB logcat -s Unity XREAL | grep -E "\[(WorldOrigin|HandPinch|CubePlacer|ImageTarget|Status)\]|XREAL|Exception"
```

`[Status]` lines repeat the messages shown in the glasses.

## Using the app

Look around and the room mesh appears and grows.

### Placing and moving cubes

Hold your hand in view of the glasses and touch your thumb and index fingertips together. A ring between them shows where the pinch is; it closes in as your fingers close and turns green when the pinch registers.

- **Pinch on or next to a cube** (within 3 cm of its surface): picks the cube up. It follows your fingers until you open them.
- **Pinch anywhere else:** places a new 5 cm orange cube centered on the pinch, already picked up. Let go right away to leave it there, or keep pinching to carry it into place.

Open your fingers fully before the next pinch. Either hand works, and each hand can hold its own cube.

Pinches are detected from the distance between the thumb and index fingertips. XREAL also recognizes hand gestures (open hand, grab, pinch, point, victory, call, system and thumbs up), but they were too unreliable to use on the S22:
- Point: most pinches were reported as pointing.
- Open palm facing up: hands were tracked poorly with the palm turned up, and an open hand is reported whenever a hand is relaxed.
- Fist: reported, but it flickered between gestures every 0.1 to 0.3 seconds and was never held long enough to tell it apart from a hand closing into a pinch.

New cubes are lined up with the world axes, so after saving to Scan A they line up with the image. Cubes stay fixed in the room when the world origin moves.

### Phone buttons

| Button | What it does |
| --- | --- |
| **Save to image** | The next scan image you look at becomes the world origin, and the room mesh and cubes are saved to it |
| **Load from image** | The next scan image you look at brings back the room mesh and cubes saved to it, and restores the origin |
| **Rescan** | Removes a loaded room mesh and resumes live scanning. Cubes stay |
| **Clear cubes** | Removes all cubes. Saved rooms keep their cubes until you save over them |
| **Clear everything** | Restarts the app, which clears everything a fresh launch would: cubes, the loaded or live room mesh, the world origin and tracking. Saved rooms stay saved. Press it twice within 3 seconds; the first press only asks for confirmation |

**Save to image** and **Load from image** turn green while they wait for an image. Press one again to cancel.

While one of them is waiting, a scan frame (four corner brackets) is shown in the middle of your view. The frame, the status messages, the outline on recognized images and the pinch ring are drawn on top of everything, so a desk or other surface closer than them doesn't hide them.

### Saving and loading with Scan A

A scan image is a picture registered in the app at build time.

1. Print `Assets/ScanTargets/ScanA.jpg` at **20 × 15 cm** and put it somewhere it can stay, flat on a desk or on a wall. XREAL works out the image's distance from its registered size, so a smaller or larger copy puts the origin, mesh and cubes in the wrong place. Showing the image on a screen at that exact size also works.
2. Scan the room and place some cubes. Press **Save to image** and look at the image through the scan frame. Only an image inside the frame is used. A blue outline appears when it's recognized, and turns green when the save is done. The origin moves to the center of the image, facing along it.
3. In a later session, press **Load from image** and look at the image through the frame. The saved room mesh replaces live scanning, the saved cubes replace any cubes you placed, and the origin comes back.

If there's no room mesh and no cubes yet, **Save to image** only sets the origin. The image must stay in the same place between sessions, or everything loaded is moved by the same amount.

**Adding images:** add them to the reference image library `Assets/ScanTargets/ScanTargets.asset`, with their physical size, and rebuild. XREAL doesn't support adding images at runtime.

### Saved data

Rooms are saved in the app's storage on the phone, in `Android/data/com.unity.template.ar_mobile/files/RoomMeshes/`, one file per scan image: `image-<image name>.roommesh`. Saving to an image replaces what was saved to it before. Uninstalling the app or changing its package ID deletes or orphans them.

Rooms saved before cubes existed still load, with no cubes. Files named after anchor IDs, and XREAL's anchor maps in the `files/` folder, are left over from the removed tagged-item feature and aren't used.

## Project layout

| Path | What it is |
| --- | --- |
| `Assets/Scenes/Main.unity` | The app's scene: XREAL rig, AR session, meshing, image tracking, world origin, hand pinch and cubes |
| `Assets/Scripts/WorldOrigin/` | `WorldOrigin` moves the XR rig so a chosen point is world `(0, 0, 0)`. `InfiniteAxes` draws the axes; its object is disabled in the scene |
| `Assets/Scripts/RoomMesh/` | `ImageTargetController` (saving to and loading from scan images), `ScanFrame` (the frame shown while waiting for an image), `PhoneControllerButtons` (the buttons on the phone's controller screen), `RoomMeshStore` (saving and restoring the mesh and cubes), `RoomMeshFile` (the save format), `StatusToast` |
| `Assets/ScanTargets/` | The scan image `ScanA.jpg` and its reference image library, `ScanTargets.asset` (Scan A, 0.20 × 0.15 m) |
| `Assets/Scripts/Interaction/` | `HandPinch` (pinches from XR Hands joint poses, and the pinch cursor), `HandJoints` (joint positions in world space) |
| `Assets/Scripts/Cubes/CubePlacer.cs` | Places a cube at a pinch in empty space, moves cubes that are pinched, and keeps track of the cubes. They use `Assets/Materials/Cube.mat` |
| `Assets/Scripts/App/AppRestart.cs`, `Assets/Plugins/Android/AppRestart.androidlib/` | **Clear everything**. XREAL can't reset tracking or clear its room mesh while running, so the app quits the way XREAL's home menu quits it, and `RestartActivity`, running in its own `:restart` process, opens it again once the old process has ended |
| `Assets/Shaders/OverlayUnlit.shader` | Unlit, vertex-colored lines drawn on top of everything, used by `Assets/Materials/OverlayLine.mat` for the scan frame, image outlines and pinch ring. The status text uses TextMesh Pro's overlay shader through `Assets/Materials/StatusText Overlay.mat` |
| `Assets/XREAL/XREALVirtualController Variant.prefab` | XREAL's phone controller with its UI camera set to **Target Eye: None**. The stock prefab sizes that camera to the glasses, so the controller is drawn shrunk and off-screen on the phone |
| `Assets/XR/Settings/XREALSettings.asset` | XREAL settings: **Input source** is **Hands**, and the link to the controller variant. Keep it Android-serialized (see [Clone and open](#2-clone-and-open-the-project)) |
| `Assets/Plugins/Android/gradleTemplate.properties` | Sets `android.uniquePackageNames=false`. Two XREAL libraries share a namespace, which Unity 6.6's Android Gradle Plugin 9 otherwise rejects |
| `Packages/com.xreal.xr/` | XREAL SDK for Unity 3.1.0, embedded |
| `Assets/Samples/` | XREAL Interaction Basics and AR Features, XRI Starter Assets and Hands Interaction Demo, XR Hands HandVisualizer. `Main.unity` uses the XREAL hands rig, with far casting turned off on its Near-Far interactors so no beams are drawn, and the meshing sample's classification script and prefabs |
| `Tool/wireless-adb.sh` | Wireless ADB setup |

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| Black screen in the glasses, black phone screen, app crashes (`SIGSEGV` in `libnr_api.so`, `NRSwapchainCreateEx`) | Android 16 / One UI 8 took over the glasses' display. Set `mirror_built_in_display` to `1` and keep DeX off; see [S22.md](S22.md) |
| App closes right away; log shows `NotFindRuntime` or `libnr_api.so not found` | The glasses aren't connected, or Control Glasses is missing, the wrong version, or was never opened. Install 3.1.0 and open it once. Right after reinstalling it, open it again before starting the app |
| Phone shows a black screen instead of the controller, but the glasses work | `XREALSettings.asset` lost its Android-only fields. Switch to Android, then set **Virtual Controller** in Project Settings → XR Plug-in Management → XREAL to `Assets/XREAL/XREALVirtualController Variant.prefab` |
| Controller shown shrunk or off to one side | The stock controller prefab is in use instead of the variant (see the row above) |
| Build fails: "XREAL command line tool fail to build an image database", `exitCode:137` | macOS quarantined XREAL's image tool, which happens if the repo was downloaded as a ZIP through a browser. Run `xattr -d com.apple.quarantine "Packages/com.xreal.xr/Tools~/MacOS/trackableImageTools"` |
| Build fails: "Namespace 'nrsdk.pack' is used in multiple modules" | `Assets/Plugins/Android/gradleTemplate.properties` is missing |
| Build And Run ends with "No activity … MAIN … LAUNCHER" | Harmless; open the app on the phone |
| No hand joints or pinch ring | Keep your hand in front of the glasses, within their view and arm's reach. The log should show `NRHandTracking START!` at launch; if not, check that **Input source** is **Hands** in Project Settings → XR Plug-in Management → XREAL |
| Pinching doesn't place or move a cube | Each pinch is logged as `[HandPinch] … pinched at …`, and each new cube or pickup as `[CubePlacer] Placed cube …` or `[CubePlacer] … picked up …`. To move a cube, start the pinch on it or within 3 cm of it. Open your fingers fully before pinching again, and keep the hand in view for a moment first: a hand that comes into view already pinched is ignored. The thresholds are on the **Hands** object in `Main.unity` |
| Scan image never gets an outline | Look at it from about 0.5–1 m in good light, with the whole image in view. Glare on a glossy print or a screen can prevent recognition |
| **Clear everything** closes the app but it doesn't come back | Open it from the launcher. The log shows `[AppRestart] Restarting`, then XREAL's `QuitApplication`; `RestartActivity` relaunches the app up to about 6 seconds later |
| Mesh, cubes or origin loaded from a scan image are offset | The image was printed at the wrong size or moved since it was saved |
| Phone keeps restarting to install updates | See [S22.md](S22.md) for how automatic updates were blocked |

## Status

- Verified on the S22:
  - 6DoF tracking, meshing, and the phone controller.
  - The SDK on Android 16, using the workaround above.
  - Saving to and loading from Scan A (2026-10-21).
  - Hand tracking and pinching (2026-11-06). It replaced the phone controller's beam, whose direction drifted after some movement.
- Verified on the S22 (2026-11-06): placing cubes, and saving and loading them with Scan A.
- Verified on the S22 (2026-11-06): XREAL's gesture IDs match the NRSDK ones (open hand 0, grab 1, pinch 2, point 3, victory 4).
- Verified on the S22 (2026-11-06): moving cubes by pinching.
- Not yet verified on a device: placing cubes by pinching in empty space, the scan frame, drawing on top of everything, and **Clear everything**. The restart activity is confirmed to be in the APK.
- Removed: tagging arbitrary items with persistent XREAL anchors. Scan A is now the only way to save and load. The anchor code is in the git history (`f4dcba5` and earlier) if it's needed again.
- The package ID is still the Unity template's `com.unity.template.ar_mobile`. Changing it moves the app's data folder, so do it before saving rooms you want to keep.
