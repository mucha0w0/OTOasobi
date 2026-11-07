# OTOasobi WinUI デザイン原則

この文書は、現行の WinUI 3 画面（`winui/`）から読み取れる UI の方針をまとめたものです。新しい画面やコントロールを足すときは、ここに沿って Fluent の見た目と、編集画面の作業密度のバランスを保ちます。

## プロダクトと言語

- ウィンドウタイトルは **「音遊び」**。ランディングでは **「オトアソビ」**（日本語）と **「OTOasobi」**（表記）を並べる。
- ユーザー向け文言は **日本語** を標準とする。メニュー・ボタン・ツールチップ・`AutomationProperties` も同じ語彙に揃える。
- 操作説明は短い体言止めや「〜します」より、**いま何が起きるか** が分かる説明（例: ランディングの段落、スライダー横の単位表示）。

## シェルとウィンドウ

- **WinUI 3** + `XamlControlsResources`（`App.xaml`）。色は **Fluent テーマリソース**（`ThemeResource`）に任せる。環境設定のアクセントカラーだけ、アクセント用ブラシの色を差し替える。明暗は起動時に決め、ウィンドウも同じ明暗にする。切り替えたときはアプリを起動し直す。
- **Mica**（`MicaBackdrop`）と **透過背景**（`Grid` / `Page` / `Frame` は `Transparent`）で、OS のウィンドウクロームと一体化する。
- **カスタム TitleBar**（`ExtendsContentIntoTitleBar`）。アイコンは `Assets/AppIcon.ico`。
- 編集作業を想定したサイズ感:
  - 最小 **1180 × 680**
  - 初期 **1540 × 840**

## 画面の切り替え

- 単一の `MainPage` 内で **Visibility** により状態を切り替える（別 Page へのナビゲーションは使わない）。
  - **IdleView** — フォルダ未選択
  - **BusyView** — 読み込み・解析中
  - **EditorView** — 波形編集
  - **ErrorView** — フォルダを開けないとき
- ランディング・エラーは **中央寄せ**、`MaxWidth` **480**（Busy は **360**）、`Padding` **24**。主アクションは **Accent** ボタン、幅 **MinWidth 160〜200**。

## 情報設計（編集画面）

編集領域は、見えているパネルで隙間なく埋める。タブや別ウィンドウにはしない。配置は二分木で、葉がパネル、枝が左右か上下とその比率。

パネルは **ファイル**、**波形**、**スペクトログラム**、**音量**、**oto区間**、**原音修正**、**oto調整**。初期状態では原音修正だけ閉じ、残りは今までの並びに近い木にする。左がファイル、その右の左がグラフの縦積み（上から波形、スペクトログラム、音量、oto区間）、右が oto調整。幅の目安はファイル約 280、oto調整約 320。

- 新しく表示するときは、面積がいちばん大きいパネルを長い辺の向きに半分にする。横長なら左右、縦長なら上下。もとあったパネルは左または上、新しいパネルは右または下。同じ広さなら、木の先にあるほうを割る。
- 閉じると、対になっていた側がその場所を受け継ぐ。
- 境目はドラッグで比率を変える。およそ 160px より狭めない。ドラッグ中は波形と原音修正の文字と値の線を隠し、絵を不透明度 55% で伸ばすだけにする。離してから描き直す。
- タイトルをドラッグすると、相手のパネルに上下左右の印が出る。印の上で離すと、その側で相手の1枚を半分にして差し込む。端へ寄せると、その端を共有するいちばん大きなまとまりを半分にして差し込む。印も端もない場所で離すと戻る。
- 表示の切り替えは、タイトルの閉じるとメニュー「表示」。配置は `LocalApplicationData` の `editor-layout.json` に保存し、「配置を初期状態に戻す」で初期の木に戻す。
- すべて閉じたときは、「表示メニューからパネルを開けます」と出す。

開いているフォルダのパスは編集画面に出さない。原音修正と oto調整の本文は `ScrollViewer`、**主要操作はパネル最下部の固定フッター**（上ボーダー + `Padding 12,10,12,12`）。グラフ 4 枚の時間範囲は 1 つで、ルーラは各パネルにある。音素推定、スペクトログラム、音量、oto設定は、カーソルが乗っているときだけホイールで前後のファイルへ移す。時間の横移動は Shift＋ホイールだけ。Ctrl＋ホイールはポインタの位置を基準に拡大する。ほかのパネルは、そのパネルの中を縦にスクロールする。タイトルバーはどのパネルも同じ Fluent の面で、高さ 20。閉じるは枠のない × だけ。グラフの中だけ、これまでの暗いキャンバス。

## グローバル chrome

- **MenuBar**（`LayerFillColorDefaultBrush`、下線 1px）にファイル・解析操作。よく使う操作には **キーボードアクセラレータ**（例: Ctrl+O, Ctrl+S）。
- **StatusBar** / **FileStatusBar**（2 段あり得る）: 同じレイヤー色・上線 1px、`Padding 16,10`。進捗は `ProgressBar` + 右側の短いテキスト（長いパスは `TextTrimming`）。
- 編集画面上部の通知は **InfoBar**（`MaxHeight` 132 の `ScrollViewer` 内、閉じられる）。成功・警告・エラーは `InfoBarSeverity` で区別。

## レイアウトのリズム

- **StackPanel.Spacing**: カード内 **8〜10**、セクション間 **10**、ランディングのブロック **4 / 8 / 16 / 28**。
- **Border.Padding**: カード **12**、パネル見出しは高さ **20**・左 **8**、メニュー周り **4,0**。
- **CornerRadius**: カード **8**、セグメント内ボタン **5**、セグメントのトレイ **8**（背景は `ControlFillColorSecondaryBrush`、内側 `ColumnSpacing 2`）。

## コンポーネントパターン

### カード（`PanelCardStyle` / `EffectCardStyle`）

- `CardBackgroundFillColorDefaultBrush` + `CardStrokeColorDefaultBrush`
- `BorderThickness 1`、`CornerRadius 8`、`Padding 12`
- 見出しは `BodyStrongTextBlockStyle`。中身は `StackPanel` + `Spacing 8`（EQ バンドなど例外は **12**）。

### ツールバー系ボタン

| スタイル | 用途 | 目安 |
| --- | --- | --- |
| `ToolbarButtonStyle` | 二次操作（保存、リセット、プリセット保存） | `MinHeight 32`、`Padding 10,6` |
| `ToolbarAccentButtonStyle` | 主要操作（oto.ini 作成、エフェクト適用） | 同上、`AccentButtonStyle` ベース、`Padding 12,6` |
| `AccentButtonStyle` | ランディング・エラーの単発 CTA | 中央配置 |

サイドパネルフッターの主要ボタンは **HorizontalAlignment Stretch**。アイコン付き CTA は `SymbolIcon` + テキストを **Horizontal** `StackPanel`、`Spacing 8`。

### ボタンの面

文字やアイコンを載せる面は、後ろが透けない不透明さにする。`ControlFill` と `CardBackground` は半透明なので、描画面の上では文字やアイコンが読みにくくなる。面は `SolidBackgroundFillColorBase` から `Quarternary`、境界は `SurfaceStrokeColorDefaultBrush`。ホバー、押下、無効も、同じ不透明な面の明暗で表す。

### スライダー行

- 1 行目: 左にラベル（通常 `TextBlock`）、右に現在値（`CaptionTextBlockStyle` + `TextFillColorSecondaryBrush`、右寄せ）。
- `Slider` は `SnapsTo="StepValues"`。秒・dB・Hz など **単位を値テキストに含める**。
- 同パターン: `OverlapValueStyle` / `BandValueStyle` / `RackButtonStyle`（EffectRack 側）。

### プリセット

- **DropDownButton**（`MinHeight 32`、横幅ストレッチ）+ `MenuFlyout`。
- プリセット名の横に変更時のみ **`*`**（`PresetDirtyMark`）。ツールチップで「プリセットから変更している」旨を示す。
- その下に **「現在の〜を保存」** 系の二次ボタン（ツールチップで保存内容を具体化）。

### セグメントタブ（`SegmentButtonStyle`）

- `RadioButton` のカスタムテンプレート。未選択は二次色、選択時は **カード風の面** + **SemiBold** + 一次色。
- Hover / Press / Focus は `SubtleFill*` と `AccentFillColorDefaultBrush` のフォーカスリング。

### エフェクトカード（`EffectRack`）

- ヘッダ行: **グリップ（任意）** | **タイトル（BodyStrong）** | **ToggleSwitch**（`OnContent`/`OffContent` は空、右マージン 8）。
- グリップは 3 本線（幅 14、高さ 2、`TextFillColorSecondaryBrush`）、ツールチップで並べ替えを説明。
- プリセットブロックはカード下部に **ラベル「プリセット」** から始める（見出しスタイルは BodyStrong より弱い通常 `TextBlock` も可 — 現行はエフェクト内で統一）。

### リスト

- `ListView`: 横スクロール無効、ファイル名は `CharacterEllipsis` + 必要なら `ToolTipService`。
- 最近のフォルダ: カード内リスト、`SymbolIcon` **Folder** + **Accent** 色。2 行目にパス（Caption、二次色）。

### モーダル（`ContentDialog`）

WinUI のコマンド領域は **左が Primary、右が Close**。Accent（プライマリ色）は `DefaultButton` に指定したボタンだけに付く。列の入れ替え（`Grid.SetColumn`）はしない。

| 役割 | スロット | 位置 | 色 | 例 |
| --- | --- | --- | --- | --- |
| 肯定 | `PrimaryButtonText` | 左 | プライマリ（`AccentButtonStyle`） | はい、保存、再解析する、上書きする |
| 否定 | `CloseButtonText` | 右 | セカンダリ（`DefaultButtonStyle`） | いいえ、キャンセル |

- 2 ボタンの確認は **`DefaultButton = Primary`**。これで左が Accent、右が通常ボタンになり、Enter も肯定側。
- 通知だけの **「閉じる」** は Close のみ。右・セカンダリのまま（`DefaultButton` は付けない）。読むだけで、確定操作ではない。

## 色とタイポグラフィ（Fluent 領域）

- **見出し階層**: `Title` → `Subtitle` → `BodyStrong` → `Body` → `Caption`（WinUI 既定スタイルをそのまま使用）。
- **意味づけ**:
  - 補助テキスト・数値 readout → `TextFillColorSecondaryBrush`
  - 無効 → コントロールの `IsEnabled` / セグメントの Disabled 状態
  - 強調・リンク的アイコン → `AccentTextFillColorPrimaryBrush` / `AccentFillColorDefaultBrush`
  - エラーアイコン → `SystemFillColorCriticalBrush`
- ページ背景色のハードコードは避け、**Layer / Card / Control / Divider** の Fluent ブラシで面と線を分ける。

## 波形エディタ（`SpanEditor`）

波形、スペクトログラム、音量、oto の描画面は、ウィンドウの明暗に合わせる。ライトでは明るい地に暗い線と文字、ダークでは暗い地に明るい線と文字。

- 区間の種類や oto の点の色は意味で固定する。ホバーは琥珀色のまま。
- スペクトログラムの画像そのものの配色は、生成したときのまま。周りの地、目盛り、ラベルはテーマに合わせる。
- エフェクターの波形（`NoiseWaveform`）は、面と線をそのパネルのテーマブラシから取る。
- コードでブラシを取るときは `AppearanceTheme.Brush` で、要素の `ActualTheme` の辞書を見る。`Application.Current.Resources` はアプリ全体のテーマなので、ここからは取らない。

## frqエディター

ピアノロール（`FrqPianoRoll`）の地、格子、目盛りは明暗に合わせます。鍵盤は白い鍵と黒い鍵のままです。点の色は意味で固定し、明暗で濃さだけ変えます。ふだんは青緑、選択中は琥珀色、音程なしは下の段の灰色です。範囲の矩形はアクセント色です。カーソル、ペン、＋12、-12、- は帯を持たず、鍵盤の右に重ねます。面は不透明です。＋12、-12、- はカーソルとペンと同じ 36×32 で、範囲選択のときだけ出します。目盛りの単位は右端です。

## 録音

録音リスト、録音、マイクモニターは、初期配置には出ません。表示メニューか、配置テンプレートの「録音」で開きます。マイクモニターは波形が 1 枚で、右にマイクのレベルメーターが付きます。未録音の行はマイクの直近 3 秒、録音済みの行はその WAV です。目盛りは 0 から -48 dB です。入力を聴くはオフが既定です。主操作はフッターの「テイクを聴く」で、再生中は「停止」になり、波形の上に再生位置が出ます。リストは台本の行、録音はいま歌う行と録音ボタンです。リストの選択はドロップダウンで、一番上は「選択しない」、その次は「ファイルから選ぶ」です。「ファイルから選ぶ」でファイルを開く画面が出ます。一度選んだリストは、次からこの中に出ます。録音リストの上から、見出し、ドロップダウン、録音済みの数とリストの行数、すべて・未録音・録音済み・リスト外です。リスト外は、リストに無いフォルダ内の WAV で、すべてには出しません。ガイドは「ファイルを選ぶ」と「自分で設定する」のラジオです。ファイルを選ぶときはドロップダウンだけで、WAV を選びます。同じ名前の txt が対になります。自分で設定するときは、音程、大きさ、カウント、録音中に鳴らすかだけを出します。停止したあとに次の未録音へ進むかは、その下のカードです。マイクの入力と、ガイドとテイクの出力は、環境設定の「録音」です。主操作はフッターの「録音」で、録音中は「停止」になります。

## アクセシビリティとヘルプ

- インタラクティブ要素に **`AutomationProperties.Name`**。複雑な操作（波形）は **`HelpText`**（例: `EditorHelp` リソース文字列）。
- **`ToolTipService.ToolTip`**: 破壊的操作・略語（oto.ini、original フォルダ）・スライダーの目安値。
- ビジー状態: `ProgressRing` / `ProgressBar` に `AutomationProperties.Name`。

## コピーとフィードバック

- ボタンラベルは **動詞**（「〜を作成」「適用」「フォルダを開く」）。メニュー項目とボタンで同じ操作なら **同じ文言**（例: oto.ini 作成）。
- 処理結果は **InfoBar のタイトル + 本文**、または **ErrorView** の中央メッセージ。技術例外は `ErrorText` にそのまま見せる場合あり。
- 解析・保存の進行は **BusyView**（モーダル的な中央）と **下部 StatusBar**（編集中のバックグラウンド処理）を使い分ける。

## 実装チェックリスト（新 UI を足すとき）

1. 色は `{ThemeResource ...}` か、SpanEditor のように **例外理由付きの固定パレット** のどちらか。
2. 設定項目は **カードにグループ化**し、スライダーは **ラベル + 右側 Caption 値**。
3. パネル右端の **主操作は Accent + フッター固定**、副操作はその上または同じフッター内の Default ボタン。
4. 横幅が溢れる文字列は **Ellipsis**、全文が必要なら **ToolTip** または **IsTextSelectionEnabled**（パス表示）。
5. スタイルは `MainPage.xaml` / `EffectRack.xaml` の既存 `Style` を **BasedOn** で再利用し、同じ `MinHeight 32` のタッチターゲットを維持する。
6. 確認モーダルは **肯定を Primary（左・Accent・`DefaultButton`）**、**否定を Close（右・通常ボタン）**。ボタン列は入れ替えない。
7. 文字やアイコンのあるボタンは、後ろが透けない不透明な面にする。描画面の上では `ControlFill` や `CardBackground` を使わない。

## 参照ファイル

| 内容 | 場所 |
| --- | --- |
| シェル・TitleBar・Mica | `winui/MainWindow.xaml` |
| 画面状態・パネルの中身・共通 Style | `winui/MainPage.xaml` |
| 編集画面の分割 | `winui/EditorDock.cs`, `winui/EditorLayout.cs` |
| エフェクト UI | `winui/EffectRack.xaml` |
| 波形・色・インタラクション | `winui/SpanEditor.cs` |
| frqエディター | `winui/FrqPianoRoll.cs`, `winui/FrqFile.cs` |
| 録音 | `winui/MainPage.Rec.cs`, `winui/MainPage.Mic.cs`, `winui/RecList.cs`, `winui/RecTone.cs`, `winui/RecCapture.cs`, `winui/RecTrace.cs` |
| ウィンドウサイズ・ドロップ | `winui/MainWindow.xaml.cs` |
