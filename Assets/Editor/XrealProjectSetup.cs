using System.IO;
using CesiumForUnity;
using Unity.XR.CoreUtils;
using Unity.XR.XREAL;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

/// <summary>
/// Project configuration, scene generation and APK build for the XREAL cube sample.
/// Each step is runnable from the XREAL menu or headless via -executeMethod.
/// </summary>
public static class XrealProjectSetup
{
    const string k_ScenePath = "Assets/Scenes/CubeScene.unity";
    const string k_ApkPath = "Build/xrealcube.apk";
    const string k_PackageName = "com.watsk.xrealcube";
    // Append your Google Maps Platform API key (Map Tiles API enabled, billing on) after "key=".
    const string k_GoogleTilesUrlPrefix = "https://tile.googleapis.com/v1/3dtiles/root.json?key=";

    [MenuItem("XREAL/1. Configure Player Settings")]
    public static void ConfigurePlayerSettings()
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.forceSDCardPermission = true;

        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, k_PackageName);
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.productName = "XREAL Cube";

        // TrackedPoseDriver lives in UnityEngine.InputSystem.XR: with the legacy-only default (0) its
        // bindings never resolve, the camera never receives a head pose, and content appears glued to
        // the view while stereo rendering still looks fine. 2 = Both, so legacy Input callers still work.
        var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0];
        var serialized = new SerializedObject(playerSettings);
        serialized.FindProperty("activeInputHandler").intValue = 2;
        serialized.ApplyModifiedProperties();

        var restoreLevel = QualitySettings.GetQualityLevel();
        for (var i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.vSyncCount = 0;
        }
        QualitySettings.SetQualityLevel(restoreLevel, false);

        AssetDatabase.SaveAssets();
        Debug.Log("[XrealProjectSetup] Player settings configured.");
    }

    [MenuItem("XREAL/2. Enable XREAL XR Loader (Android)")]
    public static void EnableXrealLoader()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget perBuildTarget))
        {
            Directory.CreateDirectory("Assets/XR");
            perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perBuildTarget, "Assets/XR/XRGeneralSettings.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuildTarget, true);
        }

        if (!perBuildTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        var manager = perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Android).Manager;
        if (!XRPackageMetadataStore.AssignLoader(manager, nameof(XREALXRLoader), BuildTargetGroup.Android))
        {
            Debug.LogError("[XrealProjectSetup] Failed to assign XREALXRLoader.");
            EditorApplication.Exit(1);
            return;
        }

        if (!EditorBuildSettings.TryGetConfigObject(XREALSettings.k_SettingsKey, out XREALSettings xrealSettings))
        {
            Directory.CreateDirectory("Assets/XR");
            xrealSettings = ScriptableObject.CreateInstance<XREALSettings>();
            AssetDatabase.CreateAsset(xrealSettings, "Assets/XR/XREALSettings.asset");
            EditorBuildSettings.AddConfigObject(XREALSettings.k_SettingsKey, xrealSettings, true);
        }

        xrealSettings.InitialTrackingType = TrackingType.MODE_6DOF;
        EditorUtility.SetDirty(xrealSettings);
        AssetDatabase.SaveAssets();

        Debug.Log($"[XrealProjectSetup] XREALXRLoader enabled for Android, tracking={xrealSettings.InitialTrackingType}, " +
                  $"stereo={xrealSettings.StereoRendering}, loaders={manager.activeLoaders.Count}.");
    }

    [MenuItem("XREAL/3. Build Cube Scene")]
    public static void BuildCubeScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var originGo = new GameObject("XR Origin");
        var origin = originGo.AddComponent<XROrigin>();

        var offsetGo = new GameObject("Camera Offset");
        offsetGo.transform.SetParent(originGo.transform, false);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(offsetGo.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 1000f;
        // On an additive see-through display black renders as transparent.
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.AddComponent<AudioListener>();

        var driver = camGo.AddComponent<TrackedPoseDriver>();
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        driver.positionInput = new InputActionProperty(
            new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        driver.rotationInput = new InputActionProperty(
            new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        driver.trackingStateInput = new InputActionProperty(
            new InputAction("Tracking State", binding: "<XRHMD>/trackingState", expectedControlType: "Integer"));

        origin.Origin = originGo;
        origin.Camera = cam;
        origin.CameraFloorOffsetObject = offsetGo;
        // Device mode puts the origin at the headset's start pose, so the cube below sits at eye level.
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
        origin.CameraYOffset = 0f;

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Cube";
        cube.transform.position = new Vector3(0f, 0f, 2f);
        cube.transform.localScale = Vector3.one * 0.3f;
        cube.AddComponent<CubeSpin>();

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, k_ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(k_ScenePath, true) };

        Debug.Log($"[XrealProjectSetup] Cube scene written to {k_ScenePath}.");
    }

    [MenuItem("XREAL/4. Add Google 3D Tileset")]
    public static void AddGoogleTileset()
    {
        var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);

        var georeferenceGo = GameObject.Find("CesiumGeoreference");
        if (georeferenceGo == null)
        {
            georeferenceGo = new GameObject("CesiumGeoreference");
            var georeference = georeferenceGo.AddComponent<CesiumGeoreference>();
            // Tokyo Station, ~50m above ground. Edit in the Inspector to point elsewhere.
            georeference.SetOriginLongitudeLatitudeHeight(139.767125, 35.681236, 50.0);
        }

        var tilesetGo = GameObject.Find("GoogleTileset");
        if (tilesetGo == null)
        {
            tilesetGo = new GameObject("GoogleTileset");
            tilesetGo.transform.SetParent(georeferenceGo.transform, false);
        }

        var tileset = tilesetGo.GetComponent<Cesium3DTileset>();
        if (tileset == null)
            tileset = tilesetGo.AddComponent<Cesium3DTileset>();

        tileset.tilesetSource = CesiumDataSource.FromUrl;
        tileset.url = k_GoogleTilesUrlPrefix;
        tileset.showCreditsOnScreen = true;
        // Stays off until GoogleTilesetApiKeyLoader.Awake appends the key and re-enables it —
        // otherwise Cesium fetches the bare (keyless) URL as soon as the tileset is enabled.
        tileset.enabled = false;

        if (tilesetGo.GetComponent<GoogleTilesetApiKeyLoader>() == null)
            tilesetGo.AddComponent<GoogleTilesetApiKeyLoader>();

        EditorSceneManager.SaveScene(scene, k_ScenePath);
        Debug.Log("[XrealProjectSetup] Google 3D Tileset added. Set the GOOGLE_MAPS_API_KEY " +
                  "environment variable before running the Editor/build; GoogleTilesetApiKeyLoader " +
                  "appends it to the tileset URL at startup so the key never lives in the scene file.");
    }

    [MenuItem("XREAL/5. Build APK")]
    public static void BuildApk()
    {
        Directory.CreateDirectory("Build");
        BakeApiKeyIntoStreamingAssets();

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { k_ScenePath },
            locationPathName = k_ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[XrealProjectSetup] Build failed: {report.summary.result}, {report.summary.totalErrors} error(s).");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[XrealProjectSetup] Build succeeded: {k_ApkPath} ({report.summary.totalSize} bytes).");
    }

    // local.env and OS env vars (used in the Editor) aren't reachable from a device build, so the
    // key has to be baked into a bundled file instead. StreamingAssets ships as-is on Android and
    // this file is regenerated fresh from local.env on every build, never hand-edited or committed.
    static void BakeApiKeyIntoStreamingAssets()
    {
        var localEnvPath = Path.Combine(Application.dataPath, "..", "local.env");
        var outPath = Path.Combine(Application.streamingAssetsPath, GoogleTilesetApiKeyLoader.StreamingAssetsKeyFileName);

        if (!File.Exists(localEnvPath))
        {
            Debug.LogWarning("[XrealProjectSetup] local.env not found; building without a Google Maps API key.");
            return;
        }

        string key = null;
        foreach (var line in File.ReadAllLines(localEnvPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("GOOGLE_MAPS_API_KEY="))
            {
                key = trimmed.Substring("GOOGLE_MAPS_API_KEY=".Length).Trim();
                break;
            }
        }

        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning("[XrealProjectSetup] No GOOGLE_MAPS_API_KEY in local.env; building without a key.");
            return;
        }

        Directory.CreateDirectory(Application.streamingAssetsPath);
        File.WriteAllText(outPath, key);
        AssetDatabase.Refresh();
        Debug.Log($"[XrealProjectSetup] Baked Google Maps API key into {outPath}.");
    }

    /// <summary>Headless entry point: full configuration, no GUI needed.</summary>
    public static void ConfigureAll()
    {
        ConfigurePlayerSettings();
        EnableXrealLoader();
        BuildCubeScene();
    }
}
