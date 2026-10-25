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

4. **Windows イベントログ記録 & 温度推移グラフ化**
   - **ログ記録の ON / OFF 切替**: 右クリックメニューや設定画面から、Windows の「アプリケーションとサービス ログ」内の専用ログ（`CPU Thermo Watcher`）への温度記録をいつでも有効/無効化できます。CPU、iGPU、各 dGPU の最高温度が個別フィールドとして同時に記録されます。
   - **推移グラフ表示**: イベントログから過去の温度履歴データを読み込み、直近1時間 / 6時間 / 24時間 / 全期間の推移を同一グラフ上に多系列プロットします。
     - 🔵 **CPU 最高温度**: DodgerBlue（青）
     - 🟢 **iGPU 最高温度**: MediumSeaGreen（緑）
     - 🟠 **dGPU #0 最高温度**: DarkOrange（橙）
     - 🔴 **dGPU #1 最高温度**: Crimson（深紅）
   - マウスホバーで該当デバイス名・時刻・温度のツールチップがポップアップし、下部サマリーには各デバイス個別の「現在 / 最高 / 最低 / 平均」統計が表示されます。

5. **Windows 11 標準の `csc.exe` でビルド可能**
   - Visual Studio や追加の .NET SDK は一切不要です。OS 標準の .NET Framework 付属コンパイラ（`csc.exe`）でダブルクリックまたは `build.bat` 1発でコンパイルできます。

---

## 🔍 Intel / AMD CPU で取得可能な温度センサーについて

> **Q. コア温度のほかにどのような温度を取得できますか？**  
> **A.** 現代の CPU は、各物理コアの温度（DTS）以外にも多数のセンサーを備えています。本ツールではこれらをすべて取得し、その中の**真の最高温度**を抽出して表示します。

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

### 💡 AMD Ryzen の各コア温度取得とマルチプロバイダー連携
AMD Ryzen（Zen 2 / Zen 3 / Zen 4 / Zen 5）は Intel CPU と異なり、各コア温度が通常の MSR レジスタに直接公開されていません。AMD 内部のコプロセッサである **AMD SMU (System Management Unit)** が PM Table（Power Management Table）として管理しています。

本ツールは以下の **マルチプロバイダー・ハイブリッド構成** を採用しており、環境に合わせて自動的に最適なセンサー群を統合・集約します。

---

## 🔌 HWiNFO / Core Temp による温度取得の仕組みと設定方法

本ツールは外部ツールが未起動でも自力（内蔵 Ring-0 ドライバー）で CPU パッケージの代表最高温度を取得できますが、世界標準のハードウェア診断ツールである **HWiNFO64** や **Core Temp** と連携することで、AMD Ryzen などの詳細な **各コア単位温度（Core 0～15）、CCD1/2温度、L3キャッシュ温度** を完全取得できます。

連携はプロセス間通信である **Windows 共有メモリ（Shared Memory）** を介して行われるため、CPU 負荷やオーバーヘッドはほぼゼロ（マイクロ秒単位で直接メモリ読み取り）です。

### 1. HWiNFO64 による温度取得方法

HWiNFO64 は、マザーボードや CPU SMU (System Management Unit) から最も高精度・網羅的に温度を取得できる業界標準ツールです。

#### 取得の仕組み
- HWiNFO64 がカーネルドライバ経由で CPU 内部の PM Table をポーリングし、メモリマップトファイル `Global\HWiNFO_SENS_SM2` にリアルタイム書き込みを行います。
- 本ツール（[HWiNFOReader.cs](file:///d:/GIT-HUB/mini_scripts/CPU%20Thermo%20Watcher/src/HWiNFOReader.cs)）はこの共有メモリを直接参照し、温度センサー（`SENSOR_TYPE_TEMP = 1`）のみを自動判別して、Core ごとの温度や CCD、ホットスポット温度を抽出します。

#### HWiNFO64 側の設定手順（必須）
1. **HWiNFO64** を起動し、**「Sensors (センサー)」** ウィンドウを開きます。
2. 画面右下の **歯車アイコン（設定 / Configure Sensors）** をクリックします。
3. 設定ダイアログの **「全般 / ユーザーインターフェース (General / User Interface)」** タブを開きます。
4. **「共有メモリのサポート (Shared Memory Support)」** のチェックボックスを **ON (有効)** にします。
5. **[OK]** をクリックして閉じます。

> [!NOTE]
> - HWiNFO64 の「共有メモリのサポート」が有効になっている間、本ツールの「センサー温度一覧」やグラフに全センサーが自動反映されます。
> - HWiNFO64 Free版（無料版）では共有メモリ機能が1回の起動につき12時間有効です（12時間経過後はHWiNFOの再起動またはPro版が必要です）。

### 2. Core Temp による温度取得方法

Core Temp は、CPU コア温度の監視に特化した軽量かつ定番のモニタリングツールです。

#### 取得の仕組み
- Core Temp が起動すると、共有メモリ `CoreTempMappingObjectEx`（または `CoreTempMappingObject`）を作成し、各コアの現在温度（`fTemp` 配列）をリアルタイムに公開します。
- 本ツール（[CoreTempReader.cs](file:///d:/GIT-HUB/mini_scripts/CPU%20Thermo%20Watcher/src/CoreTempReader.cs)）はこの共有メモリ構造体（`CoreTempSharedDataEx`）を安全にマッピングし、`Core #0` ～ `Core #N` の個別温度を瞬時に取得します。

#### Core Temp 側の設定手順
1. **Core Temp** を起動します。
2. 上部メニューの **「オプション (Options)」** → **「設定 (Settings)」** を選択します。
3. **「全般 (General)」** タブを開きます。
4. **「共有メモリを有効にする (Enable shared memory)」** にチェックを入れます（通常デフォルトで ON になっています）。
5. **[OK]** をクリックして閉じます。

> [!TIP]
> Core Temp をバックグラウンドで最小化（タスクトレイに格納）しておくだけで、本ツールが常時自動的に各コア温度を補完します。

### 3. プロバイダーの自動統合（ハイブリッド動作）

本ツール（[HardwareMonitor.cs](file:///d:/GIT-HUB/mini_scripts/CPU%20Thermo%20Watcher/src/HardwareMonitor.cs)）は起動時に利用可能なプロバイダーを自動検知し、優先度・冗長性を持って温度を統合します：

1. **HWiNFO64 が起動している場合**: 最も詳細なセンサー群（Core 0～15、CCD1/2、L3、Tctl/Tdie など最大数十項目）をフル活用。
2. **Core Temp が起動している場合**: 各コア（Core #0～#N）の個別温度を自動補完。
3. **AIDA64 が起動している場合**: 共有メモリ（`AIDA64_SensorValues`）から全センサー温度を自動補完。
4. **外部ツールが起動していない場合**: 本ツールの内蔵ドライバー（`OpenHardwareMonitorLib`）により、CPU Package / Tdie 等の物理ダイ最高温度を自力取得し、単体で継続稼働。

---

## 🛠 システム要件

- **OS**: Windows 11 (64-bit 推奨) / Windows 10
- **実行権限**: **管理者権限 (Administrator)**  
  ※CPU の MSR（Model-Specific Register）などの低レイヤーハードウェアセンサー読み取り、およびイベントソース登録に管理者権限が必要です。
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
├── OpenHardwareMonitorLib.dll        # ハードウェアセンサー読み取りライブラリ (MPL 2.0)
├── settings.json                      # しきい値温度・ログ記録等の設定ファイル (初回起動時に自動生成)
└── src/
    ├── Program.cs                     # エントリポイント・二重起動防止Mutex
    ├── Settings.cs                    # 設定データ読み書き
    ├── SettingsForm.cs                # 設定画面ダイアログ
    ├── HardwareMonitor.cs             # CPU センサー総合管理（自力取得＋各社共有メモリの自動集約）
    ├── HWiNFOReader.cs                # 業界標準 HWiNFO64 共有メモリリーダー (Core0～15, CCD1/2, L3, Tdie)
    ├── CoreTempReader.cs              # Core Temp 共有メモリリーダー (各コア温度取得)
    ├── AIDA64Reader.cs                # AIDA64 共有メモリリーダー
    ├── TrayIconManager.cs             # タスクトレイアイコン動的描画（透明/赤背景切替）& メニュー制御
    ├── SensorListForm.cs              # 右クリック「センサー温度一覧」表示画面
    ├── EventLogger.cs                 # Windows イベントログ記録 & 履歴パーサー
    └── GraphViewerForm.cs             # イベントログからの温度推移グラフ表示画面
```

### ビルド手順

1. コマンドプロンプトまたはエクスプローラーから **`build.bat`** を実行します。
2. もし `OpenHardwareMonitorLib.dll` が存在しない場合は、`build.bat` 内で `setup_lib.ps1` が自動実行され、NuGet 公式リポジトリから自動的に展開・配置されます。
3. Windows 11 標準の `csc.exe` によりコンパイルされ、`CpuThermoWatcher.exe` が生成されます。

---

## 🚀 使い方

1. **起動**:
   - 生成された `CpuThermoWatcher.exe` を実行します（`app.manifest` により起動時に管理者権限の UAC 昇格ダイアログが表示されます）。
2. **タスクトレイの確認**:
   - タスクトレイ（時計の横）に、設定で有効化された各デバイスの現在最高温度が数値アイコンでリアルタイム表示されます。
   - ※トレイの「隠れているインジケーター」に入っている場合は、ドラッグして常時表示エリアへ移動させてください。
   - **CPU**: 通常時は透明背景に白文字/黒文字。警告温度（デフォルト: 80℃）超過で赤色背景に反転。
   - **iGPU**: 有効化時、通常時は透明背景に鮮やかなエメラルドグリーン文字。警告温度（デフォルト: 85℃）超過で赤色背景に反転。
   - **dGPU #0**: 有効化時、通常時は透明背景に温かみのあるアンバー（橙色）文字。警告温度（デフォルト: 85℃）超過で赤色背景に反転。
   - **dGPU #1...**: 有効化時、コーラルピンク等の個別色で表示。
3. **右クリックメニュー**:
   - **ヘッダー**: 検出されている全デバイスの現在最高温度（例: `CPU: 82.5℃ | iGPU: 66.4℃ | dGPU #0: 52.0℃`）。
   - **センサー温度一覧 (&S)...**: CPU、iGPU、各 dGPU の全センサー温度一覧画面を表示。
   - **温度推移グラフを表示 (&G)...**: イベントログから過去の温度データを読み込んで推移グラフを描画。
   - **Windows イベントログに記録 (&L)**: 有効 / 無効 のトグル切り替え。
   - **設定 (&C)...**: トレイアイコン表示対象デバイス（CPU / iGPU / dGPU #0...）、デバイスごとの警告温度しきい値、更新間隔の変更。
   - **終了 (&X)**: アプリケーションを終了。

---

## 📊 Windows イベントログ & グラフ化仕様

### 1. イベントログの仕様
- **ログの場所（チャネル）**: **アプリケーションとサービス ログ** ＞ **`CPU Thermo Watcher`**
- **ソース名**: `CPU Thermo Watcher`（※過去バージョンで記録された `CPUThermoWatcher` および `Application` ログの履歴もグラフ表示時に自動読み込み・統合）
- **タスクのカテゴリ**: `1` (通常記録) / `2` (高温警告)
- **イベント ID**:
  - `1001`: 通常記録（定期サンプリング時の温度記録、レベル: 情報、カテゴリ: 1）
  - `1002`: 高温警告（しきい値超過時のアラート記録、レベル: 警告、カテゴリ: 2）
- **イベント内容**:
  ```
  [CPU Thermo Watcher]
  Timestamp: 2026-10-18 15:30:00
  CPU Max Temperature: 82.5 C
  CPU Max Sensor: CPU (Tctl/Tdie) (AMD Ryzen 9 7945HX)
  iGPU Temperature: 66.4 C
  iGPU Sensor: GPU 温度 (AMD Radeon 610M)
  dGPU:0 Temperature: 52.0 C
  dGPU:0 Sensor: GPU Core (NVIDIA GeForce RTX 4060)
  Status: WARNING (CPU: 82.5 C >= 80 C)

  === All Sensors ===
  - [CPU] AMD Ryzen 9 7945HX | CPU (Tctl/Tdie): 82.5 C
  - [CPU] AMD Ryzen 9 7945HX | Core0 (CCD1): 77.7 C
  ...
  - [iGPU] AMD Radeon 610M | GPU 温度: 66.4 C
  - [dGPU:0] NVIDIA GeForce RTX 4060 | GPU ホットスポット温度: 60.2 C
  - [dGPU:0] NVIDIA GeForce RTX 4060 | GPU メモリ ジャンクション温度: 66.0 C
  ...
  ```

### 2. グラフ表示機能
- 右クリックメニュー「**温度推移グラフを表示 (&G)...**」を選択するとウィンドウが開き、イベントログからデータを読み込みます。
- **表示期間の選択**: 直近1時間 / 直近6時間 / 直近24時間 / 全期間。
- **マルチ系列グラフ描画**: .NET 標準の `System.Windows.Forms.DataVisualization.Charting.Chart` を使用した折れ線グラフ。
  - 🔵 **CPU 最高温度**: DodgerBlue（ドジャーブルー）の実線と丸マーカー。
  - 🟢 **iGPU 最高温度**: MediumSeaGreen（エメラルドグリーン）の実線と丸マーカー。
  - 🟠 **dGPU #0 最高温度**: DarkOrange（ダークオレンジ）の実線と丸マーカー。
  - 🔴 **dGPU #1 最高温度**: Crimson（深紅）の実線と丸マーカー。
  - **インタラクティブ Tooltip**: プロット点にマウスカーソルを合わせると、デバイス区分・時刻・温度・測定センサー名（`[dGPU #0] 2026/10/18 15:30:00: 52.0℃ (GPU Core)`）が分かりやすくツールチップ表示されます。
- **統計サマリー**: データ件数に加え、CPU、iGPU、各 dGPU それぞれの「最高 / 最低 / 平均」温度を即座に確認できます。

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
  "EventLogIntervalSeconds": 60,
  "TextColorMode": "Auto"
}
```

- `EnabledTrayDevices`: タスクトレイに表示するデバイスIDのカンマ区切り一覧（例: `"CPU,dGPU:0"` や `"CPU,iGPU,dGPU:0"`）。
- `ShowCpuInTray` / `ShowIGpuInTray` / `ShowDGpuInTray`: 旧バージョン互換用ブール値フラグ。
- `WarningThresholdCelsius`: CPU の警告温度（赤背景にするしきい値、デフォルト: 80℃）。
- `WarningThresholdIGpuCelsius`: iGPU の警告温度（赤背景にするしきい値、デフォルト: 85℃）。
- `WarningThresholdDGpuCelsius`: dGPU の警告温度（赤背景にするしきい値、デフォルト: 85℃）。
- `UpdateIntervalMs`: 温度取得・トレイアイコン更新の間隔（ミリ秒）。
- `EnableEventLogging`: Windows イベントログへの記録を行うかどうか (`true` / `false`)。
- `EventLogIntervalSeconds`: イベントログに記録する周期（秒）。※しきい値超過時は即座に警告ログが出力されます。
- `TextColorMode`: CPU トレイアイコン文字色（`"Auto"`: タスクバーテーマ追従、`"White"`: 白文字、`"Black"`: 黒文字）。

---

## 📄 ライセンス

本プロジェクトは **修正 BSD ライセンス (3-Clause BSD License)** の元で公開されています。  
詳細は [LICENSE](file:///d:/GIT-HUB/mini_scripts/CPU%20Thermo%20Watcher/LICENSE) ファイルをご確認ください。
