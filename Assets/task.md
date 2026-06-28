# Task List — OpenFo3: ReEngine — 完全実装ロードマップ

Redot Engine 上で Fallout 3 を完全再現し、**OpenMW を超えるモダンエンジン体験** を提供するためのタスク一覧。

## 凡例

- [x] 完了
- [-] 一部実装済み/保留中
- [ ] 未完了
- [🔥] 高優先度（OpenMW を超えるための看板機能）
- [⭐] OpenMW にない革新的機能

---

## 1. アセットパイプライン（ベース）

### 1.1 ESM/ESP パース

- [x] GRUP 階層パース（ワールド・セル・トピック）
- [x] 圧縮レコード展開（ZLib）
- [x] FormId インデックス構築
- [x] マスター/プラグイン依存関係解決
- [x] 全レコードタイプのパース基盤
- [-] 全 100+ レコードタイプの完全パース対応
  - [x] STAT, DOOR, FURN, ACTI, LIGH, MSTT, CONT, MISC, WEAP, ARMO, TREE, SCOL, KEYM, ARMA, NOTE, PWAT, TACT, AMMO
  - [ ] NPC\_, CREA, RACE, LVLN, LVLC — キャラクター/レベルドリスト
  - [ ] QUST, DIAL, INFO, SCEN — クエスト/会話
  - [ ] ACHR, ACRE, REFR — 配置（NPC/クリーチャー）
  - [ ] MGEF, SPEL, EFSH — 魔法/効果
  - [ ] PERK, AVIF, GMST — ゲーム設定
  - [ ] CELL, WRLD, LAND, NAVM — ワールドジオメトリ（一部実装済み）
  - [ ] SOUN, MUSC, WTHR — オーディオ/天候
  - [ ] CSTY, GLOB, PMIS — AI/グローバル
- [ ] ESP/ESL プラグイン対応（MOD ロード順）
- [-] マスターアップデート（FormId 再マッピング）

### 1.2 BSA アーカイブ

- [x] BSA v104/v105 読み込み
- [x] ZLib 展開
- [x] 全 BSA 動的読み込み（Meshes, Textures, Misc, Voices 等）
- [ ] テクスチャ圧縮形式完全対応（DXT1/3/5, NVTT）
- [ ] ストリーミング読み込み（必要に応じて部分展開）
- [ ] BSA パッチ（BA2 形式の対応 — Fallout 4 互換）

### 1.3 NIF モデルパース

- [x] NIF バージョン 20.2.0.7
- [x] ブロック階層解決（全ノード型）
- [x] NiAVObject Flags = uint32 確定
- [x] ジオメトリ抽出（NiTriStripsData, NiTriShapeData）
- [x] 座標変換（FO3→Godot, WorldScale=0.015）
- [ ] NiSkinInstance/NiSkinData 完全対応（スキニング）
- [ ] NiSkinPartition GPU 最適化データ対応
- [ ] 全プロパティブロック対応
  - [x] BSShaderPPLightingProperty
  - [x] NiAlphaProperty
  - [x] NiTexturingProperty
  - [x] NiMaterialProperty
  - [x] NiStencilProperty
  - [x] NiSpecularProperty
  - [ ] NiWireframeProperty
  - [ ] NiFogProperty
  - [ ] NiVertexColorProperty
  - [ ] NiZBufferProperty

### 1.4 テクスチャ読み込み

- [x] DDS → Godot テクスチャ変換
- [x] テクスチャキャッシング
- [ ] 全 DDS 形式対応（BC1-BC7, R8G8, 浮動小数点）
- [ ] テクスチャストリーミング（巨大テクスチャの分割ロード）
- [ ] キューブマップ/Irradiance マップの抽出
- [ ] 手動テクスチャ差し替え（MOD/4K テクスチャパック対応）

---

## 2. レンダリングコア

### 2.1 シェーダー/マテリアルシステム

- [x] 拡散テクスチャ抽出と適用
- [x] ノーマルマップ（Environment Mask R→Roughness）
- [x] スペキュラー/グロスマップ（Environment Mask G→Metallic, R→Roughness）
- [x] 環境/反射マップ（Slot 4→MetallicTexture）
- [x] ディテールマップ（DetailBlendMode, Uv1Scale）
- [x] パララックス/ハイトマップ
- [x] アルファテスト/ブレンド完全対応
- [-] シェーダータイプ区別（15/18 タイプ）
  - [x] Default, EnvironmentMap, Glow, Heightmap, ZBufferWrite, LODLandscape, LODBuilding
  - [x] MultiLayerParallax, ParallaxOcc, SnowShader, MultiLayerParallaxOcc
  - [x] Wing, SkinTint, HairTint, ParallaxOccInner
  - [x] TallGrass, LODLandscapeNoGrass（internal）
  - [x] FO3Sky, FO3Water, FO3Unlit（internal）
  - [ ] MultiLayerParallax の完全レイヤーステッチング対応
  - [ ] LODLandscape のブレンドマップ対応
- [x] 頂点カラー適用
- [⭐] **物理ベースシェーディング全面採用**: FO3 の BSShader を PBR パラメータに完全マッピング。OpenMW の固定関数を超える品質。
- [🔥] **シェーダーエディタ連携**: マテリアル設定を Godot ShaderMaterial として動的生成し、ユーザーがリアルタイム編集可能に

### 2.2 ライティングシステム

- [x] セルライティング（XCLL）
- [x] テンプレートライティング継承マージ（LGTM + LNAM）
- [x] DirectionalLight3D + Fog
- [x] オブジェクトライト（LIGH→OmniLight3D/SpotLight3D）
- [x] シャドウマップ（PSSM, atlas_size=4096）
- [x] LightingTemplate 完全再現
- [🔥] **ダイナミックグローバルイルミネーション**: VoxelGI/SDFGI による間接光。OpenMW にない本格間接照明
- [🔥] **コンタクトシャドウ**: 細部の密着影をスクリーンスペースで生成
- [⭐] **Volumetric Fog / ライトシャフト**: Godot の Volumetric Fog を活用。メガトンの差し込む光を再現
- [ ] 減衰/減光パラメータの完全対応（FalloffExponent, FOV）
- [ ] セル間ライティングブレンド（Interior/Exterior 遷移）

### 2.3 地形/地表レンダリング

- [x] LAND レコード解析
- [x] 33x33 グリッドメッシュ生成
- [x] ギャップフィリング
- [x] 地形衝突形状
- [x] インテリアフォールバック平坦地形
- [x] LAND フィルタ（MasterFormIDIndex）
- [x] テクスチャタイリング/ブレンド
- [x] 地形 LOD（9x9 + 距離切替）
- [🔥] **ディスタントランド（遠景地形）**: OpenMW スタイルの distant statics + 地形遠景レンダリング。wasteland の地平線まで見渡せる
- [🔥] **地形テッセレーション**: Godot のシェーダーベーステッセレーションで高精細地形
- [ ] 地形テクスチャブレンド（アルファマップ/レイヤーブレンド）
- [ ] 地形テクスチャバッチング（アトラス化によるドローコール削減）
- [ ] LAND ウォーター対応（水面高さを地形データから設定）
- [ ] 地形編集ツール（ランタイム地形変更 — 爆発跡等）

### 2.4 ポストプロセス/エフェクト

- [🔥] **HDR トーンマッピング**: Filmic/ACES 対応。オリジナルのくすんだ色味からモダンな映像へ
- [🔥] **スクリーンスペースリフレクション (SSR)**: 水たまり・金属面のリアルタイム反射
- [🔥] **スクリーンスペースアンビエントオクルージョン (SSAO)**: 影の質感向上
- [🔥] **被写界深度 (DoF)**: VATS 中のフォーカス/ブラー演出
- [🔥] **モーションブラー**: 武器スイング・爆発時のブラー
- [🔥] **ブルーム/グレア**: 明るい光源・太陽のグレア
- [🔥] **カラーグレーディング**: FO3 のセピア/グリーンティントを再現しつつ調色可能に
- [ ] レンズフレア（太陽方向）
- [ ] 画面分割（CO-OP 用）
- [ ] 水中エフェクト（屈折・フォグ・ブルーティント）
- [ ] 天候エフェクト（WTHR レコード対応 — 雨・埃・曇り）
- [⭐] **Ray Tracing 対応**: Godot の Vulkan Ray Tracing パイプライン（将来的に）。影・反射・GI をパスレーシングに置換

### 2.5 パーティクル/VFX

- [x] BSStripParticleSystem → GPUParticles3D
- [x] NiParticleSystem パース
- [ ] 全パーティクル修飾子対応（NiPSEmitter, NiPSFacing, NiPSBoundUpdater 等）
- [ ] 火花/煙/雨/埃の物理パーティクル
- [🔥] **GPU パーティクル全面移行**: 10万パーティクル級の爆発・放射能雲
- [🔥] **ディゾルブ/メルトエフェクト**: FEV/エネルギーウェポンの溶解表現
- [ ] デカールシステム（弾痕・血迹・爆発跡）
- [ ] 衝突ベースパーティクル（弾丸が壁に当たると火花）

---

## 3. 衝突/物理エンジン

- [x] bhkCollisionObject / bhkRigidBody 解析
- [x] 全形状対応（Box, Sphere, Capsule, ConvexVertices, MoppBvTree, PackedNiTriStrips, List, NiTriStrips, Transform）
- [x] Half-float 展開
- [x] StaticBody3D 親子関係修正
- [x] 動的物理（MotionType → RigidBody3D/CharacterBody3D/StaticBody3D）
- [x] Havok 物理パラメータマッピング（21 種類）
- [ ] レイキャスト/ピッキング
- [🔥] **ラグドール物理**: NPC 死亡時のラグドール。Havok の拘束条件を Godot Joint にマッピング
- [🔥] **破壊可能オブジェクト**: 壁・障害物の破壊（Health/Destruction データ対応）
- [ ] Havok モーター/スプリング（ドア・跳ね橋）
- [ ] 物理マテリアルの摩擦パラメータ完全対応
- [ ] 布物理（旗・衣服）
- [ ] 流体物理（水の波紋・流れ）

---

## 4. サウンド/オーディオ

- [🔥] **全サウンド再生システム**: Wwise/FMOD 相当の動的オーディオ
- [ ] BSA からのサウンド展開（XWM/MP3/WAV）
- [🔥] **3D 空間オーディオ**: Godot のオーディオエンジン + HRTF 対応。OpenMW より高精度な定位
- [ ] 環境音のゾーン配置（AMB レコード）
- [🔥] **ディレイ/リバーブ/オクルージョン**: 屋内と屋外の音響の差。閉じたドア越しの減衰
- [🔥] **ラジオシステム**: Galaxy News Radio / Enclave Radio の選局 UI + ノイズ混信
- [ ] ボイス/会話システム（LIP ファイル対応）
- [ ] 足音/サーフェスマテリアル連動
- [ ] タイムストレッチ（VATS 中の音声ピッチダウン）
- [⭐] **ダイナミックミュージック**: 戦闘中/探索中/発見時のシームレス BGM 遷移
- [⭐] **サウンドシェーダー連携**: オーディオスペクトラムを視覚エフェクトに反映（ラジオの波形 EQ 等）

---

## 5. アニメーション/キャラクター

### 5.1 スキニング

- [x] NiSkinInstance/NiSkinData パース
- [x] スキンインスタンス紐付け
- [x] Skeleton3D 構築（NiBone 階層）
- [x] 頂点ウェイト抽出
- [ ] 頂点ウェイト SurfaceTool 適用（Redot API 拡張待ち）
- [ ] GPU スキニング（NiSkinPartition 活用）
- [ ] モーフターゲット（フェイシャルアニメーション — NiMorphMeshModifier）

### 5.2 アニメーション

- [ ] KF ファイル読み込み（NiSequence/NiControllerManager）
- [ ] アニメーションステートマシン（Idle/Walk/Run/Attack/Hit/Death）
- [ ] ブレンドツリー（上下半身分離 — 歩きながら射撃）
- [ ] 逆運動学 (IK) — 足の設置、ドアノブ操作
- [ ] ルートモーション（移動アニメーション）
- [⭐] **AI モーション生成**: 機械学習ベースの自然なモーション補間
- [🔥] **360° ムーブメント**: 8方向移動アニメーションの滑らかなブレンド

### 5.3 第三人称カメラ

- [ ] 三人称視点
- [ ] 肩越しカメラ（TPS 射撃）
- [ ] カメラ衝突（壁抜け防止）
- [ ] FOV 動的変更（ズームイン/アウト）
- [ ] DOF/ブルーム連動（焦点距離に応じた被写界深度）

---

## 6. NPC/AI

### 6.1 NPC システム

- [ ] ACHR/ACRE レコードからの NPC 配置
- [ ] レベルドリスト（LVLN/LVLC）からの動的スポーン
- [ ] NPC プロフィール（名前・種族・外見・装備）
- [🔥] **オブジェクトパイパー AI**: 障害物を回避して移動する高度な経路探索
- [ ] デイリースケジュール（AI Packages）
- [ ] フォーメーション移動（グループでのパトロール）

### 6.2 戦闘 AI

- [ ] 検知システム（視界・聴覚・感知）
- [ ] カバーシステム（遮蔽物の自動利用）
- [ ] チーム戦術（フランキング・連携射撃）
- [ ] 戦闘スタイル（接近/狙撃/魔法 — FO3 の CombatStyle 対応）
- [ ] 退却/投降/逃亡
- [ ] 怒り/フェアリー/勇気パラメータによる個性

### 6.3 会話システム

- [ ] DIAL/INFO レコードからの会話ツリー
- [ ] スキルチェック（Speech/Barber）
- [ ] 態度/好意度システム
- [ ] 音声付き会話（LIP ファイル同期）
- [ ] 日本語字幕表示（FO3 日本語版データ対応）
- [⭐] **AI 動的会話**: LLM を活用した未収録会話の動的生成（MOD 機能）

---

## 7. 戦闘/ゲームプレイシステム

### 7.1 VATS システム

- [🔥] **バットタイム**: 時間停止＋ターゲット選択 UI
- [🔥] **部位狙い**: 頭・腕・脚・胴体の個別ヒット判定
- [🔥] **命中率表示**: 距離・武器スキル・遮蔽に基づく動的確率
- [🔥] **肉弾カメラ**: 近接攻撃時のドラマチックカメラワーク
- [ ] クリティカルヒット演出
- [ ] AP 消費/回復

### 7.2 武器/防具

- [ ] 全武器種対応（拳銃・小銃・ショットガン・エネルギーの完全再現）
- [ ] ダメージ計算（DMG/DATA → ダメージタイプ・DT・DR 解決）
- [ ] 武器コンディション（劣化/修理システム）
- [ ] 弾薬タイプ（通常・徹甲・散弾・エネルギーセル）
- [ ] 照準/ADS（アイアンサイト・スコープ）
- [ ] リロードアニメーション
- [ ] 武器改造（FO3 にはないが、NV/FO4 スタイルの改造を追加）

### 7.3 S.P.E.C.I.A.L / スキル/ パーク

- [ ] 7 属性完全実装 (Strength, Perception, Endurance, Charisma, Intelligence, Agility, Luck)
- [ ] 全 13 スキル（Small Guns, Big Guns, Energy Weapons, Explosives, Lockpick, Medicine, Melee, Repair, Science, Sneak, Speech, Barter, Unarmed）
- [ ] レベルアップ UI（Perk 選択画面）
- [🔥] **全 Perk 実装**（100+ Perk）
- [ ] Perk の視覚的フィードバック（Mysterious Stranger, Grim Reaper's Sprint 等）
- [ ] Tag! スキルシステム

### 7.4 クラフト/収集

- [ ] アイテム収集/インベントリ
- [ ] 武器修理（同一武器を消費）
- [ ] 薬品調合（化学実験台）
- [ ] 弾薬製造（NV スタイル — オプション追加）
- [🔥] **ワークショップ**: FO4 スタイルの設置物建築（MOD では FO3 に逆移植実績あり）
- [ ] スクラップ/解体
- [ ] 収集品（Bubblehead, スタージェム等）の専用表示棚

---

## 8. UI/HUD

### 8.1 HUD

- [ ] 体力/AP/放射線量インジケータ
- [ ] コンパス（目的地・施設アイコン）
- [ ] 敵対状態インジケータ（警戒/戦闘/搜索）
- [ ] 武器/弾薬表示
- [ ] サブタイトル/会話テキスト
- [ ] ターゲット情報（名前・HP・状態）

### 8.2 Pip-Boy 3000

- [🔥] **完全再現 Pip-Boy UI**: 緑色 CRT モニターのスタイルを完全再現
- [ ] データタブ（ステータス/Perk/効果）
- [ ] インベントリ（武器/防具/アイテム/弾薬）
- [ ] マップ（Local/World マップ — 発見済みロケーション）
- [ ] ラジオ（選局/ON-OFF）
- [ ] クエストログ
- [ ] 統計情報
- [⭐] **Pip-Boy 3D モード**: 腕に装着した 3D モデル表示＋ホログラム
- [⭐] **Pip-Boy カスタムテーマ**: 色・フォント変更可能に

### 8.3 メニュー

- [ ] メインメニュー（Continue/New/Load/Mods/Settings/Quit）
- [ ] セーブ/ロード画面（スクリーンショット付き）
- [ ] 設定画面（グラフィック/オーディオ/コントロール/ゲームプレイ）
- [ ] MOD コンフィグメニュー
- [ ] マップメニュー（Fast Travel）

---

## 9. クエスト/ストーリー

### 9.1 クエストシステム

- [ ] QUST レコードからのクエストフル対応（全メイン+サイド+無線+DLC）
- [ ] クエストステージ/目標追跡
- [ ] 条件分岐（複数解決方法）
- [ ] クエストマーカー（コンパス/3D 空間）
- [ ] DLC クエスト（Operation Anchorage, The Pitt, Point Lookout, Mothership Zeta, Broken Steel）
- [ ] メインクエスト完全対応（Todd Howard's Vision からエンディングまで）

### 9.2 ダイアログ

- [ ] 分岐ダイアログ
- [ ] スキル/属性/クエスト状態による選択肢フィルタ
- [ ] Karma 値による反応変化
- [ ] 声優音声 + 字幕

---

## 10. ワールドシステム

### 10.1 ワールドローディング

- [x] ESM パース（GRUP 階層）
- [x] NIF モデル解析
- [x] 座標変換
- [x] 非同期ワールドローディング
- [x] キャッシング
- [x] 全 26 レコードタイプ対応
- [x] 複数ワールドスペース（F1-F9）
- [🔥] **ストリーミングワールド**: カメラ位置に応じた動的ロード/アンロード。メガトン→DC ruins→Capital Wasteland を途切れなく移動
- [🔥] **屋内/屋外シームレス遷移**: ドアを開けるとローディングなしで屋内に移動（Godot のシーンチェンジを活用）
- [ ] 内部セル（INTERIOR CELL）の完全対応
- [ ] ポータル/ルームシステム（屋内の効率的レンダリング）

### 10.2 NAVMESH / 経路探索

- [x] NAVM レコード解析
- [x] NavigationMesh 生成
- [ ] NavigationAgent3D 経路探索
- [🔥] **動的 NAVMESH**: 爆発・障害物の変化に応じたリアルタイム NAVMESH 再生成
- [ ] カバー NAVMESH（戦闘用遮蔽識別）
- [ ] ジャンプ/はしご NAVMESH（高低差移動）

### 10.3 天候/環境

- [ ] WTHR レコード完全対応
- [ ] 昼夜サイクル
- [ ] 雨/雪/砂埃（天候別パーティクル）
- [ ] 雲/空の動的レンダリング
- [ ] 風（木・草・パーティクルの連動）
- [🔥] **Volumetric Cloud**: 3D クラウド。Wasteland の空をドラマチックに
- [ ] 放射能ストーム（FO3 固有 — 緑色の嵐）

---

## 11. MOD/拡張性

### 11.1 MOD ローダー

- [🔥] **ESP/ESL ロード順管理**: プラグイン競合解決
- [🔥] **BA2/BSA パッチ適用**: テクスチャ MOD の優先順位
- [ ] 仮想ファイルシステム（MOD ファイルのオーバーライド）
- [ ] LOOT 連携（ロード順自動最適化 — 外部連携）
- [ ] MOD 管理 UI（有効/無効・優先順位変更）
- [⭐] **サンドボックス MOD 対応**: スクリプト拡張による MOD 作成。Script Extender (FOSE) 互換レイヤー

### 11.2 エディター（OpenMW-CS 相当）

- [🔥] **ワールドエディター**: 3D ビューポートでのオブジェクト配置・編集
- [ ] セル/ワールドエディター（地形編集）
- [ ] ダイアログ/クエストエディター（ビジュアルスクリプティング）
- [ ] NPC/クリーチャーエディター
- [ ] アイテム/武器エディター
- [ ] NavMesh ビジュアルエディター
- [⭐] **リアルタイム編集**: ゲームを止めずにパラメータ調整・即時反映
- [⭐] **Godot エディタ統合**: Godot のエディタ拡張として動作。シーンエディタで FO3 アセットを直接編集可能に

### 11.3 スクリプティング

- [ ] FO3 スクリプト言語のパース/実行
- [🔥] **Lua/Virtual Machine ベースのスクリプトエンジン**: 高速かつ安全なスクリプト実行
- [🔥] **FOSE (Fallout Script Extender) 互換**: 既存 MOD が動作する互換レイヤー
- [⭐] **C# スクリプティング**: Godot の強みを活かし、C# で MOD 開発可能に
- [⭐] **ビジュアルスクリプト**: Blueprint/Scratch 風のノードベーススクリプティング
- [ ] イベントシステム（OnDeath, OnHit, OnActivate 等）

---

## 12. ネットワーク/マルチプレイヤー

- [⭐] **CO-OP モード**: 2-4 人協力プレイ。ホストがワールドをホスト
- [⭐] **Dedicated Server**: 専用サーバーでの大規模マルチプレイ
- [ ] キャラクター同期（位置・アニメーション・状態）
- [ ] アイテム同期（ドロップ・トレード・インベントリ）
- [ ] 戦闘同期（ダメージ・VATS 協力）
- [ ] クエスト同期（ホストがクエスト状態を管理）
- [ ] ボイスチャット
- [ ] アンチチート/整合性チェック
- [⭐] **Tes3MP ライクな体験**: Morrowind のマルチプレイ MOD と同等の完成度

---

## 13. ツール/インフラ

### 13.1 デバッグ/プロファイリング

- [x] Python 解析ツールキット
- [x] デバッグ表示（FPS, 位置, キャッシュ）
- [ ] プロファイリングツール（フレームタイム・メモリ使用量）
- [ ] インゲームデバッグコンソール（FO3 の `~` キー相当＋α）
- [ ] オンデマンドログフィルタリング

### 13.2 設定/保存

- [x] config.json 設定ファイル
- [x] BSA ファイルリスト外部化
- [x] ワールド設定外部化
- [ ] グラフィック設定 UI
- [ ] コントロール設定（キーボード/マウス/ゲームパッド完全対応）
- [ ] セーブデータ互換（FO3 の .fos 読み込み？）
- [ ] セーブ圧縮/クラウドセーブ

### 13.3 パフォーマンス最適化

- [x] インスタンスプーリング
- [x] フラスタムカリング
- [x] Prop LOD
- [ ] オクルージョンカリング（屋内/遮蔽物）
- [ ] テクスチャアトラス化
- [ ] メッシュマージ（静的オブジェクトのバッチング）
- [ ] マルチスレッドレンダリング
- [ ] シェーダーキャッシュ（コンパイル済みシェーダーの保存）
- [🔥] **Vulkan レンダラー**: Godot の Vulkan バックエンドを最大活用。ドローコール最小化

### 13.4 エラーハンドリング

- [ ] クラッシュレポーター
- [ ] アセット欠落時のフォールバック
- [ ] セーブデータ破損検出
- [ ] ESM パースエラー耐性（部分的読み込み）

---

## 14. プラットフォーム/移植

- [ ] **Windows**: 第一ターゲット（現在）
- [ ] **Linux**: Proton/ネイティブ対応
- [ ] **macOS**: Apple Silicon 対応
- [⭐] **Android**: Godot の Android エクスポートでモバイル Fallout 3 を実現（OpenMW が Android 対応しているように）
- [⭐] **Steam Deck**: コントローラ最適化・パフォーマンスプロファイル
- [⭐] **Nintendo Switch (Homebrew)**: Godot の Switch エクスポート対応
- [ ] **Web (WebAssembly)**: ブラウザで FO3 をプレイ（概念実証）

---

## 15. Quality of Life / OpenMW を超えるモダン機能

以下は OpenMW が実現していない、または不十分な **最先端機能**。

- [⭐] **Ultrawide 対応**: 21:9 / 32:9 完全対応。UI の自動スケーリング
- [⭐] **高 FPS 対応**: 144/240fps での物理演算・アニメーションの安定動作
- [⭐] **HDR ディスプレイ対応**: リアル HDR (10-bit, PQ) 出力
- [⭐] **DLSS/FSR/XeSS 対応**: Godot のアップスケーラー連携
- [⭐] **NVIDIA Reflex / AMD Anti-Lag**: 低レイテンシ入力
- [⭐] **インタラクティブローディングスクリーン**: 3D モデル表示・TIP 表示
- [⭐] **Autosave / Quicksave 改善**: コンフリクトフリー保存
- [⭐] **キーボードリマップ + コントローラ完全対応**: コンフィグ可能な全操作
- [⭐] **アクセシビリティ**: 字幕サイズ変更・色覚モード・操作補助
- [⭐] **Mod プロファイル**: MOD セットの切り替え・エクスポート
- [⭐] **ビルトインフォトモード**: シーンフリーズ + フィルター/構図調整
- [⭐] **リプレイシステム**: 過去のプレイを録画・再生
- [⭐] **統計トラッカー**: プレイ時間・倒した敵数・距離等の詳細統計
- [⭐] **クロスセーブ**: クラウド同期（Steam Cloud / Google Drive 等）

---

## 16. 既存コード品質

### バグ修正優先度

- [x] #10 メガトンの地面がない問題
- [x] #11 ライトの向きが正しくない問題
- [-] #12 街中の建築物の向きが合わない問題
- [ ] #13 Redot 26.1 の SurfaceTool に SetBoneIndices/SetBoneWeights がない問題
- [ ] #14 大規模ワールドのロード時間が長い
- [ ] #15 一部 NIF メッシュのマテリアルが欠落（全 18 タイプ中の未対応 3 タイプ）
- [ ] #16 シャドウアクネ（近距離のシャドウノイズ）
- [ ] #17 水のレンダリング（NiWater/NiFluid 未対応）
- [ ] #18 ワールド切り替え時のメモリリーク

### リファクタリング

- [ ] Megaton.cs の分割（2000 行越え — 責務別ファイルに分割）
- [ ] 設定系の DI/ServiceLocator 導入
- [ ] テクスチャローディングの非同期化（現在は同期的な部分あり）
- [ ] エラーログの構造化
- [ ] ユニットテスト基盤（C# テストプロジェクト）
- [ ] CI/CD パイプライン（GitHub Actions）

---

## 17. 実装ログ

### 現在の進捗

**レンダリング**: 40% · **地形**: 70% · **物理**: 70% · **サウンド**: 0% · **AI**: 0% · **UI**: 0% · **ゲームプレイ**: 0% · **MOD**: 0% · **ネットワーク**: 0%

### 2026-06-19: #11 ライトの向きが正しくない問題 - 修正 (fix/light-direction)

**問題分析:**

1. **位置の二重変換**: `CreateLightNode` が受け取る `position` は `Megaton.cs.ProcessRecord` で既に Godot 座標に変換済み（`req.Position`、メッシュと同じ座標）。しかし `CreateLightNode` 内で再度 `(X, Z, -Y)` の座標変換を適用していたため位置がズレていた。
2. **LightEnergy 固定値**: すべてのライトで `LightEnergy = 1.0f`。FO3 の `FalloffExponent` が無視。
3. **範囲下限**: `Mathf.Max(radius, 1f)` により小さな光源の範囲が不自然に拡大。

**修正内容:**

- `LightingLoader.cs`:
  - 位置の二重変換を削除、`position` を直接 `Transform3D` に使用
  - `FalloffExponent` → `Attenuation` + `LightEnergy` の変換を追加
  - 範囲下限を 1.0 → 0.5 Godot 単位に緩和

---

### 2026-06-19: #10 メガトンの地面がない問題 - 修正

**問題分析:**

1. MegatonWorld は内装ワールドスペースであり、LAND レコードを持たない。または CELL に XCLC 座標がない。
2. LAND レコードがない場合 gap fill が実行されず、地形が一切生成されない。
3. MNAM セル範囲が未設定の場合、地形生成範囲が 0 セルになる。

**修正内容:**

- `TerrainBuilder.cs`: LAND レコード不在時の最終フォールバック平坦地形生成
- `Megaton.cs`: MNAM 範囲の自動拡張（最低15x15セル）

---

### 2026-06-20: マテリアルシステム再構築と機能拡充

1. **NIFMaterialBuilder.cs 構造破損修正**
   - BuildDefault のブレース不一致、浮遊コード、ApplyRefraction 欠落を修正
   - Redot 26.1 の SubsurfScatter API に対応
   - 23 errors → 0 errors に改善

2. **頂点カラー対応**
   - NiGeometryData から RGBA 頂点カラー（float×4）を抽出
   - SurfaceTool.SetColor() でメッシュに適用
   - SkinTint/HairTint のティント効果が正しく発現

3. **シェーダータイプ拡張（3タイプ追加）**
   - Wing (type 29): 半透明 + 両面レンダリング
   - SnowShader (type 14): 明るい白色ティント
   - ZBufferWrite (type 5): 標準不透明マテリアル
   - 実装済み: 15/18 タイプ

4. **ライティング改善**
   - Ambient ライトの二重適用を修正（DirectionalLight3D 削除）
   - DirectionalShadowMaxDistance を 200→300 に延長
   - PSSM スプリット調整 (0.05/0.15/0.4)
   - project.godot に shadow_atlas_size=4096 等を追加

5. **設定ファイル化**
   - config.json 新規作成（Fallout3Root 等を外部設定可能に）
   - GamePaths.cs を JSON 設定読み込み対応に改修

6. **レコードタイプ対応拡充**
   - マスターインデックスに KEYM, ARMA, NOTE, PWAT, TACT, AMMO 追加
   - 全 26 タイプ対応

7. **ドキュメント**: 7件のドキュメントを作成
8. **地形テクスチャタイル UV 修正**: FO3 基準の 256 単位タイルを正しく反映（CellSize/256=16 リピート）
9. **ビルド警告ゼロ**: 3件の不要な catch 変数を削除
10. **複数 BSA アーカイブ対応**: config.json の BSA セクションから全アーカイブを動的読み込み
11. **SCOL パーツ配置**: ONAM+DATA サブレコードの解析と個別 STAT インスタンスの生成
12. **NiSkinInstance/NiSkinData パース基盤**: FO3 バージョンのバイナリレイアウトを nifxml から検証
13. **config.json World 設定**: TargetWorld / CenterX / CenterY を外部設定化
14. **NiNode 派生型拡充**: 未対応ノードの子階層がトラバースされない問題を修正
15. **NiSkinPartition パース**: GPU スキニング最適化データのパース追加

---

### 2026-06-20: 複数ワールドスペース対応

- 全 WRLD レコード自動検出
- ワールドごとの Node3D コンテナ分離
- F1-F9 キーによるワールド切り替え
- 遅延ロード + 表示切替
- 共有キャッシュ（メッシュ, NIF, テクスチャ）の全ワールド再利用

---

### 2026-06-20: マテリアル改善 + 地形 LOD + LAND インデックス最適化

1. スペキュラー/グロスマップ改善（Environment Mask デュアルチャンネル）
2. 環境/反射マップ対応拡充（全ビルダーに展開）
3. ディテールマップ修正（DetailBlendMode, UV設定）
4. LAND インデックス共有最適化
5. 地形 LOD 実装（9x9 簡略化メッシュ + 距離切替）
6. リファクタリング（ApplyDiffuse/ApplyNormalMap 等のヘルパー分割）

---

### 2026-06-20: パフォーマンス最適化（プーリング＋フラスタムカリング＋LOD基盤）

1. インスタンスプーリング（MeshInstance3D の Rent/Return）
2. フラスタムカリング（Camera3D.IsPositionInFrustum）
3. Prop LOD 基盤（距離に応じたメッシュ差し替え）
4. ワールド切り替え時のプール解放

---

### 2026-06-20: 残り3シェーダータイプ実装（FO3 Sky / Water / Unlit）

- BSVersion検出、FO3→internal タイプ変換
- BuildFO3Sky（無光・発光）、BuildFO3Water（透明・屈折）、BuildFO3Unlit（無光・強制発光）
- Skyrim互換維持

---

### 2026-06-20: パーティクルシステム + 動的物理 + スキニング基盤

1. BSStripParticleSystem の GPUParticles3D 変換
2. 動的物理（MotionType → ボディタイプ自動選択）
3. スキニング基盤（Skeleton3D + ボーンウェイト抽出）
4. 未対応: SurfaceTool.SetBoneIndices/SetBoneWeights が Redot 26.1 に存在しないため保留

---

### 2026-06-20: 衝突形状の親子関係修正 + Havok 物理パラメータ + デバッグ表示

1. StaticBody3D 親子関係修正（MeshInstance3D→StaticBody3D の子に）
2. Havok 物理パラメータマッピング（21種類）
3. デバッグ表示（FPS, カメラ位置, キャッシュ統計）

---

### メッシュ向きバグ調査（直近セッション）

- **根本原因を特定**: `NIFBlockResolver.cs:374` の `Flags` が uint16 で読まれていた
- PyFFI で検証 → 常に uint32 で余り=0 になることを確認
- 修正後ビルド成功（0 エラー / 0 警告）
- **残課題**: Redot 上でメガトン街中の建築物の回転が合っているか視覚検証する

---

### 2026-06-21: NIFマテリアル α改善 + ESM座標解決 + LAND BTXT検証

1. **NIFMaterialBuilder α検出の拡張**: Diffuse に加えて Glow/Skin/Hair, Height/Parallax スロットでも α 検出。CullMode.Disabled 適用。
2. **ESM LAND座標解決の改善**: Cell Children GRUPs を認識し正しい座標を割り当て
3. **TerrainBuilder LAND BTXT 検証**: BTXT のない LAND をスキップ
4. **ドキュメント追加**: `Assets/project.md`

---

## ROADMAP

### Phase 1 — 基盤完成（現在地）

- [ ] 残り 3 シェーダータイプ（テクスチャアニメーション・MultiLayerParallax）
- [ ] 頂点ウェイト適用（Redot API 対応 or カスタム実装）
- [ ] 基本的な NPC 配置と歩行
- [ ] KF アニメーション再生

### Phase 2 — ゲームプレイ MVP

- [ ] 武器発射＋ダメージ（FPS 視点）
- [ ] VATS 初版（時間停止＋命中判定）
- [ ] インベントリ＋アイテム使用
- [ ] 会話システム（テキストベース）
- [ ] メインクエスト完走

### Phase 3 — モダナイズ

- [ ] HDR＋ポストプロセス完備
- [ ] ストリーミングワールド
- [ ] MOD ローダー
- [ ] CO-OP 初版

### Phase 4 — OpenMW 越え

- [ ] エディター
- [ ] マルチプレイヤー
- [ ] Ray Tracing
- [ ] Android 移植
- [ ] 全プラットフォーム対応

---

_「OpenMW が Morrowind にもたらしたものを、OpenFo3 は Fallout 3 に — そしてそれを超えて。」_
