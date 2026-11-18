using System.Collections;
using System.IO;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Appends the Google Maps Platform API key to the tileset URL at startup, so the key never
/// lives in the scene file, then enables the (initially-disabled) Cesium3DTileset. The tileset
/// shows whatever location the CesiumGeoreference origin is authored to in the scene.
/// Key source order: a "GOOGLE_MAPS_API_KEY=..." line in local.env (project root, Editor only),
/// the GOOGLE_MAPS_API_KEY OS env var (Editor only), then a StreamingAssets text file baked in at
/// build time by XrealProjectSetup.BuildApk (the only one that reaches a device build).
/// </summary>
[RequireComponent(typeof(Cesium3DTileset))]
public class GoogleTilesetApiKeyLoader : MonoBehaviour
{
    const string k_EnvVarName = "GOOGLE_MAPS_API_KEY";
    const string k_LocalEnvFileName = "local.env";
    public const string StreamingAssetsKeyFileName = "google_maps_api_key.txt";

    void Awake()
    {
        Cesium3DTileset.OnCesium3DTilesetLoadFailure += OnLoadFailure;
        StartCoroutine(PrepareAndEnableTileset());
    }

    void OnLoadFailure(Cesium3DTilesetLoadFailureDetails details)
    {
        Debug.LogError($"[GoogleTileset] Tileset load failure. type={details.type}, " +
                        $"httpStatusCode={details.httpStatusCode}, message={details.message}");
    }

    IEnumerator PrepareAndEnableTileset()
    {
        var tileset = GetComponent<Cesium3DTileset>();

        var key = ReadKeyFromLocalEnvFile() ?? System.Environment.GetEnvironmentVariable(k_EnvVarName);
        if (string.IsNullOrEmpty(key))
        {
            var path = Path.Combine(Application.streamingAssetsPath, StreamingAssetsKeyFileName);
            using var request = UnityWebRequest.Get(path);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
                key = request.downloadHandler.text.Trim();
        }

        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("[GoogleTileset] No API key found in local.env, the " +
                            $"{k_EnvVarName} env var, or StreamingAssets/{StreamingAssetsKeyFileName}; " +
                            "the Google 3D Tileset will not load.");
            yield break;
        }

        tileset.url += key;
        tileset.enabled = true;
        Debug.Log("[GoogleTileset] Tileset enabled.");
    }

    static string ReadKeyFromLocalEnvFile()
    {
        var path = Path.Combine(Application.dataPath, "..", k_LocalEnvFileName);
        if (!File.Exists(path))
            return null;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(k_EnvVarName + "="))
                return trimmed.Substring(k_EnvVarName.Length + 1).Trim();
        }

        return null;
    }
}
