# CPU Thermo Watcher

Windows 11 のタスクトレイ（通知領域）に常駐し、CPU、内蔵グラフィックス（iGPU）、および専用グラフィックス（dGPU #0, dGPU #1...）のリアルタイム最高温度を数値アイコンで個別表示・監視する軽量ユーティリティです。  
Windows 11 に標準搭載されている C# コンパイラ（`csc.exe`）のみでビルドできます。

---

## 📌 主な機能

1. **CPU / iGPU / 各 dGPU の物理的個別分離 & 最高温度抽出**
   - **CPU**: 多数ある CPU コア温度（Core #1 ～ #N）や各種センサー（Package, Tctl/Tdie, CCD, SoC 等）の中から、**常に最も高い CPU 温度** を自動判別します。
   - **iGPU (内蔵グラフィックス)**: CPU 内部に統合された AMD Radeon 610M / 780M や Intel UHD / Iris Xe などの内蔵 GPU 温度を独立して監視します。
   - **dGPU (専用グラフィックス)**: NVIDIA GeForce / AMD Radeon RX / Intel Arc などの専用グラフィックボードのコア温度、Hotspot、VRAM ジャンクション温度の中から最も高いピーク温度を判別します。複数枚の dGPU が搭載されている場合も **`dGPU #0`、`dGPU #1`... として完全に別個に認識** されます。

2. **デバイスごとのタスクトレイ数値アイコン表示 (個別 ON/OFF)**
   - 設定画面にて、検出された各デバイス（CPU、iGPU、dGPU #0、dGPU #1...）ごとに **タスクトレイ表示の ON / OFF を個別にチェックボックスで選択可能** です（デフォルト: CPU のみ表示、iGPU / dGPU は非表示）。
   - **通常時**: 背景は透明（タスクバーになじむ透過背景に高視認性フォント）。
     - ⚪ **CPU**: 白色 / 黒色（Windows 11 テーマに自動追従）
     - 🟢 **iGPU**: エメラルドグリーン（緑系）
     - 🟠 **dGPU #0**: アンバー / 橙色
     - 🔴 **dGPU #1**: コーラルピンク
   - **警告時**: 設定温度（デバイス個別の警告しきい値）を超えると、背景が **赤色** に反転して即座に高温状態を警告します。

3. **センサー温度一覧のポップアップ表示**
   - トレイアイコンを右クリックし「**センサー温度一覧 (&S)...**」を選択すると、検出されている全センサー（Core別、Package、CCD、Tctl/Tdie、GPU コア、Hotspot、VRAM など）をデバイス区分（CPU / iGPU / dGPU #0 / dGPU #1）とともにリアルタイム一覧表示します。
   - 各デバイスの最高温度センサーがそれぞれ分かりやすい専用色（CPU: 青、iGPU: 緑、dGPU #0: 橙、dGPU #1: 桃）でハイライト表示されます。

4. **自前 SQLite による超高速履歴保存 & 温度推移グラフ化**
   - **自前 SQLite データベース (`history.db`)**: Windows 11 標準の `winsqlite3.dll` を利用し、外部 NuGet や追加 DLL を一切増やさずに、全センサー温度および各デバイス代表最高温度、さらに **CPU 各コアのクロック周波数（MHz）** を 10 秒ごとの高頻度で高速記録（WAL モード）。
   - **推移グラフ表示**: 直近1時間 / 6時間 / 24時間 / 7日間 / 全期間 に加え、**「カスタム指定（開始日時～終了日時）」** によるピンポイント期間のプロット表示に対応。
     - 🔵 **CPU 最高温度**: DodgerBlue（青）
     - 🟢 **iGPU 最高温度**: MediumSeaGreen（緑）
     - 🟠 **dGPU #0 最高温度**: DarkOrange（橙）
     - 🔴 **dGPU #1 最高温度**: Crimson（深紅）
   - **自動更新 (ON / OFF)**: 新しい温度記録が書き込まれるたびにグラフを自動リフレッシュ。
   - **10℃ごとの実線グリッド**: 40℃, 50℃, 60℃... の10℃刻み位置に外枠と同じシルバー（グレー75%）の実線目盛り線を配置。

5. **CPU クロック & 温度 複合推移グラフ (クロック主体の二重 Y 軸グラフ)**
   - トレイアイコン右クリックから「**CPU クロック & 温度 複合グラフ (&M)...**」を開くと、CPU クロック周波数（MHz）を主軸、CPU 温度（℃）を副軸として重ね合わせて表示可能。
   - **1000MHz（1GHz）刻みの水平実線グリッド**: クロックを主眼に置いているため、水平基準線は温度ではなく **クロック周波数 1000MHz ごとの実線グリッド**（および 500MHz ごとの点線サブグリッド）を基準として描画されます。
   - **表示系列の個別 ON / OFF**:
     - 🔵 **全コア中 最高クロック (MHz)**: 左軸（Primary Y 軸）※個体の定格制限値ではなく、全コア中その瞬間に最も高い周波数を出しているコアのリアルタイム値
     - 🟢 **全コア 平均クロック (MHz)**: 左軸（Primary Y 軸）
     - 🌈 **指定したコアの個別クロック (Core #1 ～ #N)**: 左軸（Primary Y 軸、全選択 / 全解除ボタン付き）
     - 🔴 **CPU 最高温度 (℃)**: 右軸（Secondary Y 軸）
   - クロックと温度の連動性（サーマルスロットリングの発生状況や負荷時のブーストクロック推移）を一目で直感的に把握できます。

6. **履歴データ一覧ビューワー & CSV エクスポート**
   - トレイアイコン右クリックから「**温度履歴データ一覧 (&H)...**」を開くと、SQLite に蓄積された全データを表形式（DataGridView）で高速閲覧できます。
   - **多彩な絞り込み**: 期間（直近プリセット / 任意の日時範囲）、対象デバイス（CPU, iGPU, dGPU）、センサー名（代表最高温度のみ / 個別センサー / キーワード検索）で自在にフィルタリング。
   - **ワンクリック CSV 出力**: 絞り込んだ結果を UTF-8 (BOM付き) CSV として即座にエクスポート可能（Excel で文字化けせずそのまま分析可能）。

7. **Windows イベントログ連携 (ハイブリッド動作)**
   - グラフやビューワー用の高速テレメトリは SQLite で処理しつつ、OS レベルの監視として「高温警告アラート（Event ID: 1002）」や定期記録を Windows イベントログ（`CPU Thermo Watcher`）へ同時に記録します。

8. **Windows 11 標準の `csc.exe` でビルド可能**
   - Visual Studio や追加の .NET SDK は一切不要です。OS 標準の .NET Framework 付属コンパイラ（`csc.exe`）でダブルクリックまたは `build.bat` 1発でコンパイルできます。

---

## 🔍 Intel / AMD CPU で取得可能な温度センサーについて

> **Q. コア温度のほかにどのような温度を取得できますか？**  
> **A.** 現代の CPU は、各物理コアの温度（DTS）以外にも多数のセンサーを備えています。本ツールではこれらをすべて取得し、その中の**最高温度**を抽出して表示します。

| CPU 種別 | 主なセンサー名 | 説明 |
| :--- | :--- | :--- |
| **共通** | **Core0 ～ Core15 / Core #1 ～ #N** | 各物理コア個別のデジタル温度センサー |
| **Intel** | **CPU Package** | CPU パッケージ全体の統合温度（通常コア最高値と同等かそれ以上になりやすい重要指標） |
| **Intel** | **Core Max** | 各コア温度の中の最大値（CPU内部で集約された値） |
| **Intel** | **CPU GT Cores / Graphics** | 内蔵 GPU（iGPU）コアの温度 |
| **AMD** | **CPU (Tctl/Tdie)** | AMD Ryzen の物理ダイ統合最高温度（本ツール単体でも自力取得可能） |
| **AMD** | **CPU CCD1 / CCD2 (Tdie)** | チップレット構成の各ダイ（Core Complex Die）ごとの温度 |
| **AMD** | **Core0 ～ Core15 (CCD1/CCD2)** | 各物理コア個別の内部温度 |
| **AMD** | **L3 キャッシュ (CCD1/CCD2)** | 各 CCD に搭載された 3D V-Cache / L3 キャッシュ温度 |
| **AMD** | **CPU IOD Hotspot / 平均** | メモリコントローラ等を収めた I/O ダイのホットスポット温度 |

---

## 🔌 ハードウェア温度取得プロバイダー（ハイブリッド構成）

本ツール（[HardwareMonitor.cs](file:///d:/GIT-HUB/mini_scripts/CPU%20Thermo%20Watcher/src/HardwareMonitor.cs)）は起動時に利用可能なプロバイダーを自動検知し、優先度・冗長性を持って温度を統合します：

1. **HWiNFO64 が起動している場合**: 最も詳細なセンサー群（Core 0～15、CCD1/2、L3、Tctl/Tdie など最大数十項目）を共有メモリ経由でフル活用。
2. **Core Temp が起動している場合**: 各コア（Core #0～#N）の個別温度を共有メモリ経由で自動補完。
3. **AIDA64 が起動している場合**: 共有メモリ（`AIDA64_SensorValues`）から全センサー温度を自動補完。
4. **外部ツールが起動していない場合**: 本ツールの内蔵ドライバー（`LibreHardwareMonitorLib`）により、CPU Package / Tctl/Tdie / CCD 等の物理ダイ温度や iGPU / dGPU 温度を自力取得し、単体で継続稼働。

---

## 🛠 システム要件

- **OS**: Windows 11 (64-bit 推奨) / Windows 10
- **実行権限**: **管理者権限 (Administrator)**  
  ※CPU の低レイヤーハードウェアセンサー読み取り、およびイベントソース登録に管理者権限が必要です。
- **ビルド環境**: Windows 11 標準搭載 .NET Framework 4.x の `csc.exe`  
  （通常パス: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）

---

## 🏗 プロジェクト構成 & ビルド方法

### ファイル構成
```
CPU Thermo Watcher/
├── LICENSE                            # 修正 BSD ライセンス (3-Clause BSD)
├── README.md                          # 本書
├── build.bat                          # csc.exe によるビルドバッチ
├── csc.rsp                            # csc.exe 用コンパイラ引数定義ファイル
├── setup_lib.ps1                      # 依存ライブラリの自動取得スクリプト
├── app.manifest                       # 管理者権限昇格・UI用マニフェスト
├── LibreHardwareMonitorLib.dll        # ハードウェアセンサー読み取りライブラリ (MPL 2.0)
├── history.db                         # SQLite 履歴データベース (自動生成)
├── settings.json                      # しきい値温度・ログ記録等の設定ファイル (初回起動時に自動生成)
└── src/
    ├── Program.cs                     # エントリポイント・二重起動防止Mutex
    ├── Settings.cs                    # 設定データ読み書き
    ├── SettingsForm.cs                # 設定画面ダイアログ
    ├── HardwareMonitor.cs             # CPU/GPU センサー総合管理（自力取得＋各社共有メモリの自動集約）
    ├── HistoryDatabase.cs             # winsqlite3.dll による超高速 SQLite アクセス
    ├── HistoryViewerForm.cs           # 表形式履歴ビューワー & CSV エクスポート画面
    ├── HWiNFOReader.cs                # HWiNFO64 共有メモリリーダー
    ├── CoreTempReader.cs              # Core Temp 共有メモリリーダー
    ├── AIDA64Reader.cs                # AIDA64 共有メモリリーダー
    ├── TrayIconManager.cs             # タスクトレイアイコン動的描画 & メニュー制御
    ├── SensorListForm.cs              # 右クリック「センサー温度一覧」表示画面
    ├── EventLogger.cs                 # Windows イベントログ記録 & 履歴パーサー
    ├── GraphViewerForm.cs             # 温度推移グラフ表示画面 (任意期間・自動更新対応)
    ├── CpuCompositeGraphForm.cs       # CPU クロック & 温度 複合推移グラフ (二重軸・系列選択対応)
    └── UiHelper.cs                    # UI 共通ヘルパー・自然順ソート比較器
```

### ビルド手順

1. コマンドプロンプトまたはエクスプローラーから **`build.bat`** を実行します。
2. もし `LibreHardwareMonitorLib.dll` が存在しない場合は、`build.bat` 内で `setup_lib.ps1` が自動実行され、NuGet 公式リポジトリから自動的に展開・配置されます。
3. Windows 11 標準の `csc.exe` によりコンパイルされ、`CpuThermoWatcher.exe` が生成されます。

---

## 🚀 使い方

1. **起動**:
   - 生成された `CpuThermoWatcher.exe` を実行します（管理者権限で実行されます）。
2. **タスクトレイの確認**:
   - タスクトレイ（時計の横）に、設定で有効化された各デバイスの現在最高温度が数値アイコンでリアルタイム表示されます。
   - **CPU**: 通常時は透明背景に白文字/黒文字。警告温度（デフォルト: 80℃）超過で赤色背景に反転。
   - **iGPU**: 有効化時、エメラルドグリーン文字。警告温度（デフォルト: 85℃）超過で赤色背景に反転。
   - **dGPU #0**: 有効化時、アンバー（橙色）文字。警告温度超過で赤色背景に反転。
3. **右クリックメニュー**:
   - **ヘッダー**: 検出されている全デバイスの現在最高温度（例: `CPU: 82.5℃ | iGPU: 66.4℃ | dGPU #0: 52.0℃`）。
   - **センサー温度一覧 (&S)...**: CPU、iGPU、各 dGPU の全センサー温度一覧画面を表示。
   - **温度推移グラフを表示 (&G)...**: SQLite から高速に読み込んで推移グラフを描画（任意期間指定・自動更新対応）。
   - **CPU クロック & 温度 複合グラフ (&M)...**: CPU 最高温度と最高/平均/コア別クロックを二重軸で重ね合わせて描画。
   - **温度履歴データ一覧 (&H)...**: 表形式で全センサー温度を閲覧、日時・デバイス・センサー名での絞り込み、CSV エクスポートが可能。
   - **Windows イベントログに記録 (&L)**: 有効 / 無効 のトグル切り替え。
   - **設定 (&C)...**: トレイアイコン表示対象デバイス、警告温度しきい値、更新間隔の変更。
   - **終了 (&X)**: アプリケーションを終了。

---

## ⚙ 設定項目 (`settings.json`)

アプリケーションと同じフォルダ内の `settings.json`（または右クリックの「設定」画面）で変更できます。

```json
{
  "UpdateIntervalMs": 2000,
  "WarningThresholdCelsius": 80.0,
  "WarningThresholdIGpuCelsius": 85.0,
  "WarningThresholdDGpuCelsius": 85.0,
  "ShowCpuInTray": true,
  "ShowIGpuInTray": false,
  "ShowDGpuInTray": false,
  "EnabledTrayDevices": "CPU",
  "EnableEventLogging": true,
  "EventLogIntervalSeconds": 10,
  "TextColorMode": "Auto"
}
```

---

## 📄 ライセンス

本プロジェクトは **修正 BSD ライセンス (3-Clause BSD License)** の元で公開されています。  
詳細は [LICENSE](file:///d:/GIT-HUB/mini_scripts/CPU%20Thermo%20Watcher/LICENSE) ファイルをご確認ください。
