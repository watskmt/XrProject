# Google Maps 3D 表示機能 仕様書

## 1. 概要・目的

XREAL AR グラス向けアプリ(`XrProject`)に、指定した実在の地点の3D地形・建物モデル(Google Photorealistic 3D Tiles)を**表示するだけ**の最小機能を追加する。検索やナビゲーションなどのインタラクションは含まない。

## 2. 背景

- プロジェクト: `XrProject`(Unity 6000.5.4f1)
- XREAL SDK 3.1.0 を Android AR グラス向けに `Packages/com.xreal.xr` として埋め込み済み
- レンダーパイプラインは Built-in RP(URP/HDRPではない)、ビルドターゲットは Android(minSdk 29)、プロダクト名 "XREAL Cube"
- 3D表示は **Cesium for Unity** パッケージ経由で実現する。Cesium ion を介さず、Google Cloud の Map Tiles API キーを直接 `Cesium3DTileset` に指定する方式を採用(理由: Cesium ion アカウント不要でシンプル)

## 3. スコープ(対象)

- 指定した緯度・経度・高度を中心とした3Dタイルの読み込みと表示
- 既存の XR Origin カメラリグ(ステレオAR表示、`StereoRendering: 2`)上での描画
- Google 利用規約に基づくアテンション表示(Show Credits On Screen)

## 4. 対象外(非スコープ)

- 検索、現在地取得、ルート案内
- POI(地点情報)表示・タップ等のインタラクション
- 複数地点の切り替えUI
- オフラインキャッシュ・オフライン動作

これらは将来拡張の候補として第10章に記載する。

## 5. 前提条件・外部依存

| 項目 | 状態 |
|---|---|
| Google Cloud プロジェクト + Map Tiles API 有効化 + 課金設定 + APIキー | 未取得(ユーザーが別途取得) |
| `com.cesium.unity` 1.24.0(scoped registry: `https://unity.pkg.cesium.com`) | `Packages/manifest.json` に追加済み、Unity Editor起動による解決は未検証 |
| ネットワーク接続 | 必須(オフライン動作は非対応) |

## 6. 機能要件

1. `CesiumGeoreference` コンポーネントで表示原点(緯度・経度・高度)を設定する
2. `Cesium3DTileset` コンポーネント(Tileset Source: `From URL`)で以下のURLからタイルを読み込む
   ```
   https://tile.googleapis.com/v1/3dtiles/root.json?key=API_KEY
   ```
3. `Show Credits On Screen` を有効化し、画面上にGoogleの帰属表示(アテンションクレジット)を行う
4. 配置先は既存の `Assets/Scenes/CubeScene.unity`(または複製した新シーン)

## 7. 非機能要件

- Android実機(XREALグラス)でのタイルストリーミング負荷・フレームレートは本仕様確定時点では未計測。要検証。
- APIキーはシーンファイルに直書きせず、Inspector入力などローカルな方法で管理する(リポジトリへの誤コミットを避ける)

## 8. 既知のリスク・要検証事項

- Cesium for Unity の Android ビルドは、コミュニティで `CesiumForUnityNative` 関連の独自ビルドエラー事例が報告されている
- XREAL SDK 側の AAR namespace 衝突問題(8つの `nr_*.aar` が `package="nrsdk.pack"` を共有し Gradle でエラーになる既知の問題、パッチ適用済み)とは別事象だが、Android実機ビルド時に両方を合わせて確認する必要がある
- Built-in RP と Cesium の Shader Graph 依存パッケージの組み合わせ動作は未検証

## 9. 受け入れ基準(検証方法)

- [ ] Unity Editor 上で Play し、指定座標の3Dタイルがステレオカメラ視点で表示されること
- [ ] 画面上にGoogleのアテンションクレジットが表示されること
- [ ] (任意・次段階)Android実機ビルドが成功し、XREALグラス上で同様に表示されること

## 10. 今後の拡張候補(参考・今回は非対象)

- 地点切り替えUI
- 現在地連動表示
- POI(地点情報)表示
- 検索機能
