# XrProject 現状仕様書（Web 移植向け）

**対象リビジョン**: 2026-07-19 時点の `~/XrProject`（git 未管理のためコミットハッシュ無し）
**作成日**: 2026-11-18

---

## 1. この文書の位置づけ

本書は XREAL AR グラス向け Unity アプリ `XrProject` の **as-is（現状）仕様**であり、**ブラウザ（Web）で同等機能を再実装するための入力**として書かれている。

読者は Unity を知らない Web 実装者を想定する。そのため各節で次を区別して記述する:

- **必須の振る舞い** — 移植先でも満たすべき仕様
- **Unity / XREAL 固有の実装手段** — 現行の足場であり、移植先では不要または別手段に置き換わるもの

### スコープ

| 区分 | 内容 |
|---|---|
| **必須** | F-1 3Dタイル表示（§5）、F-2 ルート飛行（§6） |
| **参考記録** | F-3 周回カメラ（§7）— 現行シーン未使用 |
| **非スコープ** | ステレオ AR 表示、ヘッドトラッキング（Web 版は単眼3Dビュー前提） |

### 既存ドキュメントとの関係

`docs/google-3d-tiles-spec.md` は **2026-07-18 時点の初期計画書**であり、現状と以下が乖離している。歴史的経緯として保存されており、本書は**それを置き換えない**。現状の正解は本書を参照のこと。

| 初期計画書の記載 | 実際（本書） |
|---|---|
| Unity 6000.5.4f1 | 2022.3.62f3 |
| `StereoRendering: 2`（SinglePassInstanced） | `0`（MultiPass）— 2 ではタイルが描画されない |
| API キー「未取得」 | 取得済み・実機動作確認済み |
| Cesium パッケージ解決「未検証」 | 解決済み |
| 「表示するだけ、インタラクションなし」 | 周回カメラ・544km ルート飛行を実装 |

---

## 2. アプリ概要

実在地点の写実的3D都市モデル（Google Photorealistic 3D Tiles）を表示し、**国道1号 日本橋 → 大阪（実測 544.4 km）を地表追従で自動飛行する3Dビューア**。

- ユーザー入力は**一切無い**（起動すると自動再生が始まり、終点で停止）
- UI は Google の著作権クレジット表示のみ
- 検索・現在地取得・POI 表示・地点切り替えは実装されていない

---

## 3. 現行実装のインベントリ

### 3.1 技術スタック

| 項目 | 実測値 | 移植区分 |
|---|---|---|
| Unity | 2022.3.62f3 / Built-in Render Pipeline | Unity固有 |
| Cesium for Unity | 1.24.0（scoped registry `https://unity.pkg.cesium.com`） | **代替可** → CesiumJS |
| XREAL XR Plugin | 3.1.0（`Packages/com.xreal.xr` に埋め込みパッケージとして配置） | 不要 |
| 外部サービス | Google Map Tiles API（Photorealistic 3D Tiles） | **必須** |
| ビルドターゲット | Android / IL2CPP / ARM64 / OpenGLES3 / minSdk 29 / Portrait | 不要 |
| 識別子 | package `com.watsk.xrealcube` / product name "XREAL Cube" | 不要 |
| 成果物 | `Build/xrealcube.apk` 292 MB（2026-07-19 21:56 生成） | 不要 |

`Packages/manifest.json` の依存は Cesium の他に `com.unity.inputsystem` 1.7.0 / `com.unity.ugui` 1.0.0 / `com.unity.xr.management` 4.7.0 と標準モジュール群のみ。

### 3.2 シーン構成

シーンは `Assets/Scenes/CubeScene.unity` の **1枚のみ**（名前は Cube 時代の名残で、現在 Cube は存在しない）。

```
Directional Light            Light(Directional), intensity 1, rotation (50, -30, 0)
CesiumGeoreference           CesiumGeoreference + CesiumCameraManager
└─ GoogleTileset             Cesium3DTileset + GoogleTilesetApiKeyLoader
XR Origin                    XROrigin + RouteFlyThrough
└─ Camera Offset
   └─ Main Camera            Camera + AudioListener + TrackedPoseDriver
```

### 3.3 スクリプト一覧

| ファイル | 役割 | シーンでの使用 |
|---|---|---|
| `Assets/Scripts/RouteFlyThrough.cs` | ルート飛行（§6） | **使用中**（XR Origin にアタッチ） |
| `Assets/Scripts/GoogleTilesetApiKeyLoader.cs` | API キー注入（§11） | **使用中**（GoogleTileset にアタッチ） |
| `Assets/Scripts/OrbitAroundPoint.cs` | 周回カメラ（§7） | **未使用**（孤立） |
| `Assets/Scripts/CubeSpin.cs` | Cube の自転 | **未使用**（孤立、Cube 自体が無い） |
| `Assets/Editor/XrealProjectSetup.cs` | 設定・シーン生成・ビルド（§12） | Editor 専用 |

---

## 4. 外部サービス仕様（Google Map Tiles API）

**本節が移植にあたって最も重要**。レンダラが変わっても、この契約は変わらない。

### 4.1 エンドポイント

```
https://tile.googleapis.com/v1/3dtiles/root.json?key=API_KEY
```

ルートタイルセットを1回取得すれば、以降の子タイル要求はレンダラが自動で行う。

### 4.2 著作権クレジット表示（必須義務）

Google の規約上、**コピーライト帰属表示に対応したレンダラを使用し、クレジットを画面上に表示することが必須**。Google は CesiumJS 1.91 以降 / Cesium for Unreal 1.12 以降を要件充足レンダラとして挙げている。

現行実装では `Cesium3DTileset.showCreditsOnScreen = true` で実現している。

### 4.3 セッション上限（⚠️ 移植時に必ず影響する）

> 1回のルートタイルセットリクエストから、**少なくとも3時間分**のタイルリクエストが可能。この上限に達した後は、改めてルートタイルセットをリクエストする必要がある。

現行シーンの設定（15 m/s）では全行程に **約10時間**かかるため（§6.5）、この上限を複数回超える。現行 Unity 実装はこれを扱っていない（通し再生を実機で行っていないため顕在化していない）。→ §9.1 参照。

### 4.4 API キーの制限方式

現行は Google Cloud 側で **API キーの IP 制限を解除**して運用している（制限が有効だと実機からのタイル要求が 403 になるため）。

**Web ではキーがブラウザに露出するため、IP 制限ではなく HTTP リファラ制限に置き換えること。** → §9.2 参照。

### 4.5 その他

- ネットワーク接続が**必須**。オフライン動作は非対応（タイルは都度ストリーミング）
- Map Tiles API の有効化と課金設定が必要
- 利用規約・Map Tiles API ポリシーの確認が必要（EEA 所在の開発者には 2025-07-08 以降の別条件が適用される）

---

## 5. 機能仕様 F-1: 3Dタイル表示【必須】

### 5.1 振る舞い

1. 指定した緯度・経度・楕円体高を表示原点とする
2. その地点の Google Photorealistic 3D Tiles を読み込み、カメラ視野に応じて LOD を切り替えながらストリーミング表示する
3. 画面上に Google の著作権クレジットを表示する

### 5.2 パラメータ実測値と CesiumJS 対応

`Assets/Scenes/CubeScene.unity` の `Cesium3DTileset` 実測値:

| Unity (`Cesium3DTileset`) | 実測値 | CesiumJS での相当 | 移植時の扱い |
|---|---|---|---|
| `_tilesetSource` | `FromUrl`(1) | `Cesium3DTileset.fromUrl(url)`<br>または `createGooglePhotorealistic3DTileset()` + `GoogleMaps.defaultApiKey` | **必須** |
| `_url` | `https://tile.googleapis.com/v1/3dtiles/root.json?key=`<br>（キーは実行時に追記、§11） | 同じ URL | **必須** |
| `_showCreditsOnScreen` | `1`（true） | `showCreditsOnScreen`<br>**既定 false のため明示的に true にすること** | **必須** |
| `_maximumScreenSpaceError` | `16` | `maximumScreenSpaceError`（既定 16・**同値**） | 設定不要 |
| `_maximumCachedBytes` | `536870912`（512 MB） | `cacheBytes`（既定 536870912・**同値**） | 設定不要 |
| `_maximumSimultaneousTileLoads` | `20` | **相当プロパティ無し**（Cesium Native 固有） | 移植不要 |
| `_preloadAncestors` / `_preloadSiblings` | `1` / `1` | 概念が異なる（`preloadWhenHidden` / `preloadFlightDestinations`） | 既定のままで可 |
| `_loadingDescendantLimit` | `20` | 相当プロパティ無し | 移植不要 |
| `_enableFrustumCulling` | `1` | 既定で有効 | 設定不要 |
| `_forbidHoles` | `0` | `forbidHoles`（既定 false・同値） | 設定不要 |
| `_ionAssetID` / `_ionAccessToken` | `0` / 空 | — | **Cesium ion は使用しない**（Google キー直指定方式） |

> Unity 側の明示値は大半が CesiumJS の既定値と一致するため、**移植時に意図的に設定すべきは URL とクレジット表示のみ**。

### 5.3 表示原点

`CesiumGeoreference` が WGS84 の原点を保持し、その地点を Unity のワールド原点にマップする（ローカル ENU 座標系を張る）。実測値:

| フィールド | 値 | 意味 |
|---|---|---|
| `_latitude` / `_longitude` / `_height` | `43.06289` / `141.3537` / `100` | 札幌（⚠️ 実行時には無効 — §13.1） |
| `_originPlacement` | `1` = CartographicOrigin | 緯度経度高度で原点を指定 |
| `_originAuthority` | `0` = LongitudeLatitudeHeight | 緯度経度高度を正とする（ECEF は派生値） |
| `_scale` | `1` | スケール変換なし |

`CesiumCameraManager` は `_useMainCamera: 1` / `_useSceneViewCameraInEditor: 1`。どのカメラの視錐台を LOD 選択に使うかを Cesium に伝えるコンポーネントで、**CesiumJS では `Scene` が自動で担うため移植不要**。

---

## 6. 機能仕様 F-2: ルート飛行【必須】

`Assets/Scripts/RouteFlyThrough.cs` の仕様。以下はエンジン非依存の記述。

### 6.1 ルートデータ

| 項目 | 値 |
|---|---|
| 出典 | OpenStreetMap の国道1号ジオメトリを graph-route して簡略化 |
| 形式 | `(経度, 緯度)` の倍精度ペア列（折れ線 / polyline） |
| 点数 | **868 点**（= 867 セグメント） |
| 始点 | `(139.774490, 35.683855)` 日本橋 |
| 終点 | `(135.501499, 34.698086)` 大阪 |
| 総延長 | **544,372 m（544.4 km）**（Haversine, R=6371000 による実測） |
| セグメント長 | 最小 35.7 m / 中央値 433.0 m / 最大 9,896.2 m |

データは `RouteFlyThrough.cs` 内の `static readonly double2[] k_Route`（L40–907）にハードコードされている。**移植時はこの配列をそのまま JSON / GeoJSON LineString に抽出して使うこと**（再ルーティングすると経路が変わり、§14 の同等性が崩れる）。

### 6.2 制御パラメータ

シーンに保存された実測値は**スクリプト既定値と異なる**。正解はシーン側:

| パラメータ | シーン実測値 | スクリプト既定値 | 意味 |
|---|---|---|---|
| `speedMetersPerSecond` | **15**（54 km/h） | 150 | ルート上の進行速度 |
| `heightAboveSurfaceMeters` | **100** | 20 | 真下の地表から保つクリアランス |
| `loop` | `false` | false | 終点到達後にループするか |
| `raycastStartHeight` | `6000` | 6000 | 地表計測レイの開始高度 |
| `heightSmoothTime` | `1.0` | 1.0 | 高度変化の平滑化時定数（秒） |

### 6.3 アルゴリズム（疑似コード）

```
# --- 初期化 ---
segment        = 0        # 現在セグメントのインデックス
distIntoSegment = 0       # 現セグメント始点からの進行距離 [m]
currentHeight  = 60       # 楕円体高の初期推定値（日本橋の地表 +20m 相当）
yVelocity      = 0        # 高度平滑化用の速度状態
hasSurface     = false    # 一度でも地表を計測できたか

原点を route[0] に設定（高度 currentHeight）

# --- 毎フレーム ---
on_frame(dt):
    # (1) 高度制御: 真下の地表を実測し、クリアランスを一定に保つフィードバック制御
    groundOffset = 真下方向に地表を計測した結果（視点基準の相対高さ, 通常は負値）
    if 地表が取得できた:
        targetHeight = currentHeight + (groundOffset + heightAboveSurfaceMeters)
        if hasSurface:
            currentHeight = SmoothDamp(currentHeight, targetHeight,
                                       yVelocity, heightSmoothTime, dt)
        else:
            currentHeight = targetHeight        # 初回は即座に合わせる
        hasSurface = true
    # 地表が取れない（タイル未ロード）場合は currentHeight を変更しない = 現高度を保持

    # (2) 折れ線上を前進
    distIntoSegment += speedMetersPerSecond * dt
    segLen = haversine(route[segment], route[segment + 1])
    while distIntoSegment >= segLen:
        distIntoSegment -= segLen
        if segment >= len(route) - 2:           # 最終セグメントを走り終えた
            if loop:
                segment = 0
            else:
                distIntoSegment = 0
                break                           # 終点に到達 → 位置を保持して停止
        else:
            segment += 1
        segLen = haversine(route[segment], route[segment + 1])

    # (3) 現在位置と「少し先」の位置を線形補間で求める
    f       = segLen > 0 ? distIntoSegment / segLen : 0
    a, b    = route[segment], route[segment + 1]
    current = a + (b - a) * f
    ahead   = a + (b - a) * min(1.0, f + 0.05)   # セグメント長の 5% 先

    # (4) 視点位置を current（高度 currentHeight）に移動
    # (5) 視点の向きを current -> ahead の方向に向ける（水平成分のみ、ロールなし）
```

### 6.4 高度制御の補足

**「地表からの相対計測」であることが本質。** 絶対標高（DEM や楕円体高）を使っていない。Google の 3D Tiles は建物も含むメッシュなので、計測結果は「地面または建物の屋根の高さ」になる。これにより箱根峠（約 850 m）のような標高変化でもクリアランスが一定に保たれる。

タイルのストリーミング遅延で地表が計測できない瞬間があるため、(a) 未取得時は現高度を保持、(b) 取得時も `SmoothDamp` で平滑化、の2段で高度の跳ねを抑えている。**この2つは移植先でも必須**（省くと飛行がガタつく）。

`SmoothDamp` は臨界減衰バネによる平滑化。移植先で同等の挙動を得るための式:

```
omega  = 2 / smoothTime
x      = omega * dt
expTerm = 1 / (1 + x + 0.48*x^2 + 0.235*x^3)
change = current - target
temp   = (velocity + omega * change) * dt
velocity = (velocity - omega * temp) * expTerm
result   = target + (change + temp) * expTerm
```

### 6.5 所要時間

| 速度 | 所要時間 |
|---|---|
| 15 m/s（シーン実測値） | 36,291 秒 = **10.08 時間** |
| 150 m/s（スクリプト既定値） | 3,629 秒 = **1.01 時間** |

§4.3 の3時間上限と照合すること（§9.1）。

### 6.6 Unity 固有の実装手段（移植先では不要）

以下は **float 精度という Unity 固有の制約への対処**であり、振る舞いの一部ではない:

- **原点シフト**: 544 km を単精度 float のワールド座標で移動すると精度が崩壊するため、視点（XR Origin）を Unity ワールド原点に固定したまま、`CesiumGeoreference` の原点を毎フレーム前進させている（Cesium の `CesiumOriginShift` と同じ手法）。結果として「地形が動き、視点は止まっている」という実装になっている
- **地表計測**: `Physics.Raycast` を `(0, 6000, 0)` から真下へ長さ 12,000 m で発射。タイルセットが physics mesh / collider を生成していることが前提（Cesium for Unity の既定で有効）
- **向き**: 5% 先の点を `CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed()` で ECEF に変換 → `georeference.TransformEarthCenteredEarthFixedPositionToUnity()` で Unity 座標へ → Y 成分を 0 にして `Quaternion.LookRotation(dir, Vector3.up)`

### 6.7 `georeference` の解決

`RouteFlyThrough.georeference` はシーンで未設定（`fileID: 0`）。`Awake()` で `GetComponentInParent` → `FindObjectOfType` の順に自動探索する。XR Origin は `CesiumGeoreference` の子ではないため、実際には `FindObjectOfType` で解決されている。

---

## 7. 機能仕様 F-3: 周回カメラ【参考記録】

`Assets/Scripts/OrbitAroundPoint.cs`。**現在シーンにアタッチされていない**ため動作しないが、仕様として記録する。

ある地点を中心に、水平円軌道で周回しながら中心を注視する。地表からのクリアランスは §6.4 と同じ相対計測方式。

| パラメータ | 既定値 | 意味 |
|---|---|---|
| `radiusMeters` | 150 | 中心（原点）からの水平半径 |
| `heightAboveSurfaceMeters` | 50 | 真下の地表から保つクリアランス |
| `degreesPerSecond` | 6（= 1周/分） | 周回角速度。正 = 上から見て反時計回り |
| `faceCenter` | true | 中心を注視し続けるか |
| `raycastStartHeight` | 3000 | 地表計測レイの開始高度 |
| `heightSmoothTime` | 0.6 | 高度平滑化の時定数（秒） |

アルゴリズム: 角度を `degreesPerSecond × dt` 加算し、`(cos θ × r, y, sin θ × r)` に視点を置く。`y` は真下計測した地表 + クリアランスを `SmoothDamp` で平滑化した値。`faceCenter` 時は原点方向を水平に向く。

F-2 と異なり**原点シフトを使わない**（周回半径 150 m なら float 精度で足りるため）。

---

## 8. Web 移植対応表

§5.2 のタイルセットパラメータに加えて、振る舞い側の対応:

| 現行の手段（Unity / Cesium for Unity） | Web での相当（CesiumJS） | 備考 |
|---|---|---|
| `Physics.Raycast` で地表高を毎フレーム計測 | `Scene.sampleHeight(cartographic)` → 高さを返す<br>`Scene.clampToHeight(cartesian)` → 地表にスナップした位置を返す | **同期・3Dモード限定・描画中のタイルのみ対象**。`scene.sampleHeightSupported` / `clampToHeightSupported` の確認が必要。毎フレーム呼ぶ現行挙動に最も近い |
| 同上（より高精度が必要な場合） | `Scene.sampleHeightMostDetailed(positions)`<br>`Scene.clampToHeightMostDetailed(cartesians)` | **非同期（Promise）・最大詳細 LOD を使用**。毎フレーム駆動には不向きだが、未ロード時の取りこぼしを減らせる |
| 原点シフトによる float 精度回避（§6.6） | **不要** — CesiumJS は `Cartesian3`（倍精度）でグローバル座標を直接扱う。カメラを `Cartesian3.fromDegrees(lon, lat, height)` に直接置けばよい | **移植で消える複雑性**。「視点が動き、地形は静止」という素直な実装に戻せる |
| `Quaternion.LookRotation`（進行方向を向く） | `camera.setView({ destination, orientation: { heading, pitch, roll } })` | `heading` は2点間の方位角から算出:<br>`θ = atan2(sin Δλ · cos φ₂, cos φ₁ · sin φ₂ − sin φ₁ · cos φ₂ · cos Δλ)`<br>`pitch = 0`, `roll = 0`（水平維持） |
| `Mathf.SmoothDamp` | 自前実装（§6.4 の式） | |
| `Time.deltaTime` 駆動の `Update()` | `requestAnimationFrame` または `scene.preUpdate` イベント | フレーム間の実経過時間を使うこと（固定 dt 仮定は不可） |
| `CesiumGeoreference` | **不要**（グローバル座標を直接使用） | |
| `CesiumCameraManager` | **不要**（`Scene` が自動処理） | |
| `TrackedPoseDriver` + `XROrigin` + MultiPass ステレオ | **非スコープ**（単眼ビュー） | §10 に記録のみ |
| Haversine によるセグメント長 | 同じ式を実装、または `EllipsoidGeodesic.surfaceDistance` | 現行は球近似（R=6371000）。同等性のため**球近似のまま移植することを推奨** |

---

## 9. Web 移植で新たに必要になる対応

現行 Unity 実装に存在しない、Web 固有の必須対応。

### 9.1 ルートタイルセットの3時間上限【最重要】

§4.3 の通り、1回の `root.json` リクエストでタイル要求できるのは約3時間分。

- シーン実測の 15 m/s では全行程 **10.08 時間**で、上限を3回以上超える
- 超過すると以降のタイル要求が失敗し、地形が表示されなくなる（= §6.4 の地表計測も失敗し続け、高度が保持されたまま進む）

**いずれかの対応が必要:**

1. 一定時間（例: 2.5時間）ごとに `root.json` を再取得し、タイルセットを作り直す（表示の継ぎ目をどう隠すかの設計が必要）
2. 速度を上げて全行程を3時間以内に収める（150 m/s なら約1時間）
3. 走破を前提とせず、区間再生に仕様を変更する

どれを選ぶかは**仕様上の判断**であり、本書では決定しない。

### 9.2 API キーの露出対策

ブラウザ実装ではキーがクライアントに露出する。

- **最低限**: Google Cloud 側で **HTTP リファラ制限**を設定する（現行の IP 制限解除状態をそのまま持ち込まない）
- キーを隠す必要がある場合はサーバ側プロキシ経由にする（ただし Google のポリシー適合性を確認のこと）
- いずれの場合もキーをリポジトリにコミットしないこと（§11 と同じ原則）

### 9.3 クレジット表示の明示的有効化

`showCreditsOnScreen` は CesiumJS の既定が `false`。**明示的に `true` にしないと §4.2 の義務を満たさない。**

---

## 10. 非機能要件・XREAL 固有の既知制約【記録目的】

Web 移植では不要だが、現行実装の文脈として残す。

### 10.1 実機固有の落とし穴（いずれも欠けると黒画面）

1. **起動は Nebula ランチャーから行うこと。** `adb am start` で起動すると XREAL の `DynamicDDSService` が "appName is not found in cache" を出してアクティビティを suspend し、Unity エンジンが初期化されない
2. **`StereoRendering` は MultiPass（`0`）必須。** SinglePassInstanced（`2`）では Cesium の Shader Graph 製タイルセットシェーダ（`Cesium/CesiumDefaultTilesetShader`）が描画されない（Unity 標準シェーダのオブジェクトは描画されるため「キューブだけ映ってタイルが出ない」という症状になる）
3. **AAR の重複を避ける。** `Packages/com.xreal.xr/Runtime/Plugins/Android/` で Unity 2022.3 では `nractivitylife-release.aar` を有効・`nractivitylife_6-release.aar` を無効にする（両方有効だと Gradle が `Duplicate class ai.nreal.activitylife.*` で失敗）

### 10.2 カメラ・レンダリング設定

| 項目 | 値 | 理由 |
|---|---|---|
| `nearClipPlane` / `farClipPlane` | 0.1 / 1000 | — |
| `clearFlags` | `SolidColor`、背景色 黒 | 加算合成シースルーディスプレイでは黒が透明として描画される |
| 既定インターフェース向き | Portrait | — |
| vSync | 全 Quality レベルで無効（`vSyncCount = 0`） | — |
| `activeInputHandler` | `2`（Both） | `TrackedPoseDriver` は `UnityEngine.InputSystem.XR` にあり、レガシー専用（`0`）ではバインディングが解決されずヘッドポーズが来ない |
| `XREALSettings.InitialTrackingType` | `0` | — |
| `XREALSettings.SupportMultiResume` | `1` | — |

### 10.3 その他

- ネットワーク必須（オフライン非対応）
- APK サイズ 292 MB（Cesium Native + IL2CPP による）
- 実機でのフレームレート・ストリーミング負荷は未計測

---

## 11. 秘密情報の管理方式

### 11.1 キーの解決順（`GoogleTilesetApiKeyLoader.cs`）

起動時（`Awake`）に以下の順でキーを探し、**`tileset.url` の末尾に追記してから**タイルセットを有効化する:

1. プロジェクトルートの `local.env` の `GOOGLE_MAPS_API_KEY=...` 行（**Editor のみ**）
2. OS 環境変数 `GOOGLE_MAPS_API_KEY`（**Editor のみ**）
3. `StreamingAssets/google_maps_api_key.txt`（`UnityWebRequest` で読む。**実機ビルドに届くのはこれだけ**）

### 11.2 設計意図（移植先でも踏襲すべき原則）

- **キーをシーンファイル / ソースに保存しない。** シーンに保存される `_url` はキー無しの状態（`...root.json?key=`）
- **タイルセットは初期状態で無効（`enabled = false`）。** キーを追記した後に有効化する。先に有効化すると Cesium がキー無し URL を即座にフェッチして失敗する
- `Cesium3DTileset.OnCesium3DTilesetLoadFailure` を購読し、`httpStatusCode` と `message` をログ出力する（403 の切り分けに有用）
- キーが1つも見つからない場合はエラーログを出して何もしない（タイルセットは無効のまま）

### 11.3 ビルド時の焼き込み

`XrealProjectSetup.BuildApk()` が `BakeApiKeyIntoStreamingAssets()` を呼び、**毎回 `local.env` から `StreamingAssets/google_maps_api_key.txt` を再生成する**（手で編集しない）。`local.env` が無い場合は警告を出してキー無しでビルドする。

### 11.4 gitignore

以下は `.gitignore` 済み:

```
/local.env
/[Aa]ssets/[Ss]treamingAssets/google_maps_api_key.txt
/[Aa]ssets/[Ss]treamingAssets/google_maps_api_key.txt.meta
```

> 本書にキーの実値は記載しない。

---

## 12. ビルド手順【記録目的】

`Assets/Editor/XrealProjectSetup.cs` が Unity の `XREAL` メニューに5項目を登録する。

| メニュー | メソッド | 内容 |
|---|---|---|
| 1. Configure Player Settings | `ConfigurePlayerSettings()` | Android / IL2CPP / ARM64 / OpenGLES3 / minSdk 29 / package 名 / `activeInputHandler = 2` / vSync 無効 |
| 2. Enable XREAL XR Loader (Android) | `EnableXrealLoader()` | `XRGeneralSettings` 生成、`XREALXRLoader` 割り当て、`InitialTrackingType = MODE_6DOF` |
| 3. Build Cube Scene | `BuildCubeScene()` | ⚠️ **破壊的操作** — 下記参照 |
| 4. Add Google 3D Tileset | `AddGoogleTileset()` | `CesiumGeoreference`（東京駅 139.767125 / 35.681236 / h50）と `GoogleTileset` を生成し、`GoogleTilesetApiKeyLoader` を付与 |
| 5. Build APK | `BuildApk()` | キー焼き込み → `Build/xrealcube.apk` を出力 |

`ConfigureAll()` は 1〜3 をまとめて実行するヘッドレス用エントリポイント（`-executeMethod` で呼べる）。

> ⚠️ **「3. Build Cube Scene」は `CubeScene.unity` を空シーンから作り直す破壊的操作。** 実行すると現在の `CesiumGeoreference` / `GoogleTileset` / `RouteFlyThrough` の設定がすべて失われ、回転する Cube が置かれた初期状態に戻る。
>
> ⚠️ 「4. Add Google 3D Tileset」が設定する原点は**東京駅**で、現在シーンの値（札幌）とも実行時の値（日本橋）とも異なる。

`Tools/patch-xreal-aar-namespaces.sh` は Unity 6000.5 時代に AAR の namespace 衝突を回避するために作られたスクリプトで、**2022.3 では不要**（§13.3）。

---

## 13. 現行実装の既知の不整合

移植前に仕様判断が必要な点。

### 13.1 シーンの表示原点が実行時に無効

シーンの `CesiumGeoreference` には**札幌**（lat 43.06289 / lon 141.3537 / h 100）が保存されているが、`RouteFlyThrough.Awake()` が原点を `k_Route[0]`（**日本橋** 139.774490 / 35.683855）に上書きするため、**札幌の値は実行時には一切効かない**。Editor の非再生時の見た目にしか影響しない。

→ **「意図した表示地点はどちらか」が仕様として未確定。** 移植時に決めること。札幌を表示したい意図があったなら、ルート飛行とは別モードとして切り出す必要がある。

### 13.2 孤立スクリプト2本

`CubeSpin.cs` と `OrbitAroundPoint.cs` はシーンにアタッチされていない。周回カメラ（§7）を移植対象に含めるかは §1 のスコープ通り「参考記録」とする。

### 13.3 その他

- `Tools/patch-xreal-aar-namespaces.sh` は Unity 2022.3 では不要な遺物
- シーン名 `CubeScene.unity` は内容（Cube 無し・3Dタイル表示）と一致していない
- **プロジェクトが git 未管理**（`.gitignore` はあるが `.git` が無い）。実装経緯の履歴が残っていない

---

## 14. 受け入れ基準（Web 版が「同等」と言える条件）

- [ ] 指定した緯度経度の Google Photorealistic 3D Tiles が表示される
- [ ] Google の著作権クレジットが画面上に表示される（§4.2）
- [ ] §6.1 の 868 点のルートを、始点（日本橋）から終点（大阪）まで走破する
- [ ] 飛行中、真下の地表から約 100 m のクリアランスが維持される
- [ ] カメラが常に進行方向（水平）を向いている
- [ ] 標高変化（箱根峠 約 850 m）を越えてもクリアランスが維持される（絶対標高ではなく相対計測であること）
- [ ] タイルのストリーミング遅延時に高度が破綻しない（未ロード時の保持 + 平滑化、§6.4）
- [ ] 走破に3時間以上かかる設定の場合、`root.json` の上限を超えてもタイル表示が継続する（§9.1）
- [ ] API キーに HTTP リファラ制限が設定されている（§9.2）
- [ ] API キーがリポジトリにコミットされていない（§11.4）

---

## 付録: 事実のソース

本書の数値はすべて以下から実測した。

| ファイル | 取得した情報 |
|---|---|
| `Assets/Scripts/RouteFlyThrough.cs` | 飛行アルゴリズム（L916–979）、ルート 868 点（L40–907）、Haversine（L983–992） |
| `Assets/Scripts/OrbitAroundPoint.cs` | 周回カメラ仕様 |
| `Assets/Scripts/GoogleTilesetApiKeyLoader.cs` | キー解決順、タイルセット遅延有効化 |
| `Assets/Editor/XrealProjectSetup.cs` | Player 設定値、XR ローダー、シーン生成内容、APK ビルド、キー焼き込み |
| `Assets/Scenes/CubeScene.unity` | 階層と全 serialize 実測値（§5.2 / §5.3 / §6.2 の根拠） |
| `ProjectSettings/ProjectVersion.txt` | Unity 2022.3.62f3 |
| `ProjectSettings/ProjectSettings.asset` | product 名、minSdk、向き |
| `Packages/manifest.json` | Cesium 1.24.0、scoped registry、依存一覧 |
| `Packages/com.xreal.xr/package.json` | XREAL XR Plugin 3.1.0 |
| `Assets/XR/XREALSettings.asset` | `StereoRendering: 0`、`InitialTrackingType: 0` |
| `.gitignore` | 秘密情報の除外設定 |
| `docs/google-3d-tiles-spec.md` | 初期計画書との乖離点（§1） |

ルート総延長・所要時間は 868 点の座標列に Haversine（R=6371000）を適用して算出した。

外部仕様の出典:

- [Photorealistic 3D Tiles — Google Maps Platform](https://developers.google.com/maps/documentation/tile/3d-tiles)（エンドポイント、クレジット表示義務、3時間上限）
- [Cesium3DTileset — CesiumJS リファレンス](https://cesium.com/learn/cesiumjs/ref-doc/Cesium3DTileset.html)（`maximumScreenSpaceError` 既定 16、`cacheBytes` 既定 536870912、`showCreditsOnScreen` 既定 false、`fromUrl`）
- [Scene — CesiumJS リファレンス](https://cesium.com/learn/cesiumjs/ref-doc/Scene.html)（`sampleHeight` / `clampToHeight` / `*MostDetailed` の仕様）
