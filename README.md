# XrProject

XREAL AR グラス向けの Unity アプリ。Google Photorealistic 3D Tiles を表示し、国道1号（日本橋 → 大阪、544 km）を地表追従で自動飛行する。

- **Unity 2022.3.62f3**（Built-in Render Pipeline）
- **Cesium for Unity 1.24.0**（Google Map Tiles API キーを直接指定。Cesium ion は使用しない）
- **XREAL XR Plugin 3.1.0**
- ビルドターゲット: Android / IL2CPP / ARM64 / minSdk 29

仕様は [`docs/current-spec-web-port.md`](docs/current-spec-web-port.md) を参照（Web 移植向けに書かれた現状仕様書だが、現行実装の仕様書としても使える）。[`docs/google-3d-tiles-spec.md`](docs/google-3d-tiles-spec.md) は初期計画書で、内容は現状と乖離している。

---

## クローン後に必要なセットアップ

このリポジトリには、ライセンス上および容量上の理由で**コミットされていないものが2つある**。どちらも無いとビルドできない。

### 1. XREAL XR Plugin 3.1.0 を配置する

XREAL の第三者製 SDK（約 210 MB、95 MB の AAR を含む）は再配布しないため除外している。

1. [XREAL Developer Center](https://developer.xreal.com/) から XREAL SDK 3.1.0 を取得する
2. 埋め込みパッケージとして `Packages/com.xreal.xr/` に展開する（`Packages/com.xreal.xr/package.json` が存在する状態にする）

`Packages/manifest.json` には記載されていない（埋め込みパッケージはパスで解決される）ため、配置するだけでよい。

> **Unity 2022.3 固有の注意**: `Packages/com.xreal.xr/Runtime/Plugins/Android/` の AAR は、`nractivitylife-release.aar` を **有効**・`nractivitylife_6-release.aar` を **無効**にする必要がある（`_6` 付きは Unity 6 用）。両方有効だと Gradle が `Duplicate class ai.nreal.activitylife.*` で失敗する。

### 2. Google Maps Platform の API キーを用意する

1. Google Cloud で **Map Tiles API** を有効化し、課金を設定してキーを発行する
2. プロジェクトルートに `local.env` を作る:

   ```
   GOOGLE_MAPS_API_KEY=<your key>
   ```

`local.env` と、ビルド時に生成される `Assets/StreamingAssets/google_maps_api_key.txt` は両方 gitignore 済み。**キーをシーンやソースに直接書かないこと** — `GoogleTilesetApiKeyLoader` が起動時に URL へ追記する設計になっている。

> キーに IP 制限を掛けると実機からのタイル要求が 403 になる。制限を外すか、デバイスの IP を許可すること。

---

## ビルド

Unity Editor の `XREAL` メニューから実行する。

| メニュー | 内容 |
|---|---|
| 1. Configure Player Settings | Android / IL2CPP / ARM64 / OpenGLES3 / minSdk 29 等を設定 |
| 2. Enable XREAL XR Loader (Android) | `XREALXRLoader` を有効化、6DoF トラッキング |
| 3. Build Cube Scene | ⚠️ **破壊的** — 下記参照 |
| 4. Add Google 3D Tileset | `CesiumGeoreference` と `GoogleTileset` を生成 |
| 5. Build APK | `local.env` からキーを焼き込み、`Build/xrealcube.apk` を出力 |

> ⚠️ **「3. Build Cube Scene」は `Assets/Scenes/CubeScene.unity` を空シーンから作り直す。** 実行すると現在の `CesiumGeoreference` / `GoogleTileset` / `RouteFlyThrough` の設定がすべて失われ、回転するキューブが置かれた初期状態に戻る。通常のビルドでは **5 のみ**を実行すること。

ヘッドレスでのビルド:

```sh
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath . -buildTarget Android \
  -executeMethod XrealProjectSetup.BuildApk -logFile build.log
```

## 実機での実行

**必ず XREAL グラス内の Nebula ランチャーから起動すること。** `adb shell am start` で起動すると XREAL の `DynamicDDSService` がアクティビティを suspend し、Unity エンジンが初期化されず黒画面になる。

また `Assets/XR/XREALSettings.asset` の `StereoRendering` は **MultiPass（`0`）**でなければならない。SinglePassInstanced（`2`）では Cesium の Shader Graph 製タイルセットシェーダが描画されず、「標準シェーダのオブジェクトだけ映ってタイルが出ない」症状になる。

## 構成

```
Assets/
  Editor/XrealProjectSetup.cs        設定・シーン生成・APK ビルド
  Scripts/
    RouteFlyThrough.cs               ルート飛行（868 ウェイポイント内蔵）※使用中
    GoogleTilesetApiKeyLoader.cs     API キー注入               ※使用中
    OrbitAroundPoint.cs              周回カメラ                 ※未使用
    CubeSpin.cs                      キューブ自転               ※未使用
  Scenes/CubeScene.unity             唯一のシーン（キューブは既に無い）
docs/                                仕様書
Tools/patch-xreal-aar-namespaces.sh  Unity 6000.5 時代の遺物（2022.3 では不要）
```
