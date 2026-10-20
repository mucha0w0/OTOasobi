# OTOasobi WinUI デザイン原則

この文書は、現行の WinUI 3 画面（`winui/`）から読み取れる UI の方針をまとめたものです。新しい画面やコントロールを足すときは、ここに沿って Fluent の見た目と、編集画面の作業密度のバランスを保ちます。

## プロダクトと言語

- ウィンドウタイトルは **「音遊び」**。ランディングでは **「オトアソビ」**（日本語）と **「OTOasobi」**（表記）を並べる。
- ユーザー向け文言は **日本語** を標準とする。メニュー・ボタン・ツールチップ・`AutomationProperties` も同じ語彙に揃える。
- 操作説明は短い体言止めや「〜します」より、**いま何が起きるか** が分かる説明（例: ランディングの段落、スライダー横の単位表示）。

## シェルとウィンドウ

- **WinUI 3** + `XamlControlsResources`（`App.xaml`）。アプリ全体の色は **Fluent テーマリソース**（`ThemeResource`）に任せ、独自の ResourceDictionary で色体系を上書きしない。
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

編集時は **3 列** の固定幅レイアウト:

| 列 | 幅 | 役割 |
| --- | --- | --- |
| 左 | 280px | ファイル一覧（ヘッダに件数） |
| 中央 | `*` | 選択ファイル名 + `SpanEditor`（波形・区間） |
| 右 | 320px | サイドパネル（タブ切替） |

列の区切りは `DividerStrokeColorDefaultBrush` の **1px 縦線**。開いているフォルダのパスは編集画面に出さない。

右パネルは **セグメントタブ**（`原音修正` / `oto調整`）。本文は `ScrollViewer`、**主要操作はパネル最下部の固定フッター**（上ボーダー + `Padding 12,10,12,12`）。

## グローバル chrome

- **MenuBar**（`LayerFillColorDefaultBrush`、下線 1px）にファイル・解析操作。よく使う操作には **キーボードアクセラレータ**（例: Ctrl+O, Ctrl+S）。
- **StatusBar** / **FileStatusBar**（2 段あり得る）: 同じレイヤー色・上線 1px、`Padding 16,10`。進捗は `ProgressBar` + 右側の短いテキスト（長いパスは `TextTrimming`）。
- 編集画面上部の通知は **InfoBar**（`MaxHeight` 132 の `ScrollViewer` 内、閉じられる）。成功・警告・エラーは `InfoBarSeverity` で区別。

## レイアウトのリズム

- **StackPanel.Spacing**: カード内 **8〜10**、セクション間 **10**、ランディングのブロック **4 / 8 / 16 / 28**。
- **Border.Padding**: カード **12**、パネル見出し **16,12,16,8** または **12,10**、メニュー周り **4,0**。
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

## 波形エディタ（`SpanEditor`）— 意図的な例外

中央の編集キャンバスだけは **DAW 風の固定ダーク UI**（約 `#161616`）で、ライト/ダークの Fluent ページとは切り離す。

- 波形・スペクトログラム・区間オーバーレイは **意味論的な色**（無音 / 子音 / 母音 / ホット状態など）をコード側で保持。
- oto 関連のハイライトは **ティール系**（`OtoRegionBrush`）、ホバー・ドラッグは明るさや **Accent** テーマブラシでフィードバック。
- 読み取り専用の時間 readout は小さめ（例: 13px）の明るい文字、バッジは半透明ダーク背景。
- 分割線のドラッグ中は可能な限り `AccentFillColorDefaultBrush` 等の **ThemeResource** にフォールバック（`ThemeBrush` ヘルパー）。

新規のキャンバス描画を足すときも、**Fluent カード内に埋め込むコントロール** と **SpanEditor 内のピクセル美学** を混ぜない。

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

## 参照ファイル

| 内容 | 場所 |
| --- | --- |
| シェル・TitleBar・Mica | `winui/MainWindow.xaml` |
| 画面状態・レイアウト・共通 Style | `winui/MainPage.xaml` |
| エフェクト UI | `winui/EffectRack.xaml` |
| 波形・色・インタラクション | `winui/SpanEditor.cs` |
| ウィンドウサイズ・ドロップ | `winui/MainWindow.xaml.cs` |
