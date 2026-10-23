# エージェント向けメモ

別チャットが毎回ソースを辿らなくて済むための実装メモです。利用者向けの仕様は `README.md`、色・余白・ダイアログ・スライダーの見た目は `DESIGN.md` にあります。`DESIGN.md` のパネル一覧は古いです。初期配置の正は `EditorLayout.CreateDefault` です。

ユーザー向けの文言は日本語です。コミットは頼まれたときだけです。

`README.md` と`AGENT.md` は、内容の更新に伴って随時編集し、最新の状態を保つこと。  
`DESIGN.md` の内容も、実装方式に応じて編集する。

## 置き場所

- `winui/` — WinUI 3。名前空間は `Otoasobi`。画面は `MainPage` ひとつで、別 Page へは遷移しません。状態は `IdleView` / `BusyView` / `EditorView` / `ErrorView` の Visibility です。
- `otoasobi/` — 解析の Python。画面は `AnalysisClient` が `python -m otoasobi.worker` を起動し、UTF-8 の JSON 行で話します。
- `tests/OtoParity/` — xUnit。WinUI のロジック用 `.cs` をプロジェクト参照ではなく `Compile Include` で直にコンパイルします。新しい純粋ロジックをテストするときは、この csproj にもリンクを足します。`MainPage` と XAML はテストに入りません。
- `tests/test_*.py` — Python 側のテストです。
- アプリデータは `%LocalAppData%\Otoasobi`。パネル配置は `editor-layout.json` です。

`MainPage` は partial に分かれています。

| ファイル | 中身 |
| --- | --- |
| `MainPage.xaml` / `MainPage.xaml.cs` | パネルの置き場、表示メニュー、ドックの取り付け、コマンドの有効状態 |
| `MainPage.Save.cs` | セッション、フォルダを開いたときの `AdoptFolder` |
| `MainPage.OtoTable.cs` | oto の表 |
| `MainPage.Cvvc.cs` | CVVC |
| `MainPage.Frq.cs` | `.frq` 生成 |
| `MainPage.Bank.cs` | 音源の各種設定 |
| `MainPage.LayoutTemplates.cs` | 配置テンプレート |

## パネルを足す

編集画面のパネルはタブにも別ウィンドウにもしません。`EditorDock` の葉です。配置の分割はドックが行うので、パネル側で画面を割りません。

1. `EditorLayout.PaneIds` に短い id を足す。ここに無い id は `Show` が無視します。保存済みの `editor-layout.json` に新しい id が無くても、そのファイルは無効になりません。単にそのパネルが出ないだけです。
2. 初期画面に出すかは `CreateDefault()` だけが決めます。頼まれない限りここへは入れません。初期で閉じているのは、エフェクター `source`、oto数値表 `otoParams`、`.frq生成` `frq`、音源の各種設定 `bank` です。
3. `MainPage.xaml` の `PaneHolder`（`Visibility="Collapsed"` の置き場）に、中身の `Grid` を足す。起動時に `MountDock` がここから外してドックへ入れます。
4. 表示メニューに `ToggleMenuFlyoutItem` を足す。`Tag` は id、`Click` は `PaneMenu_Click`、`Text` はパネルタイトルと同じ日本語です。初期で開くものだけ `IsChecked="True"` にします。
5. `MountDock` の2か所を足す。`PaneHolder` から外す配列と、`Dock.Mount` の `(id, タイトル, 要素)` です。`SyncPaneMenu` に `IsChecked = Dock.Contains(id)` を足します。
6. 処理が長いときは `MainPage.*.cs` の partial に分けます。同じ `partial class MainPage` です。
7. ボタンの `IsEnabled` は `UpdateCommands` に書きます。解析中は `_busy` です。
8. 開く動作は `Dock.Show` です。面積がいちばん大きいパネルを、長い辺の向きに半分にします。最小の一辺は `EditorLayout.MinPane`（160）。

中身の型は次のとおりです。

- 設定のまとまりは `PanelCardStyle`。カード見出しの説明は `InfoNote` の `Title` と `Text` です。`Text` が空だとヘルプボタンは出ません。
- 項目名は通常の `TextBlock` です。その項目だけ説明があるときは、ラベルと `InfoNote`（`Title` なし、`Label` に項目名、`Text` に説明）を横一列にします。説明をカードの本文へは書きません。
- 「押して確定」する主要操作は、パネル最下部のフッターです。上ボーダー、`Padding="12,10,12,12"`、`ToolbarAccentButtonStyle`、幅は Stretch。音源設定は編集のたびに保存するので、このフッターの保存ボタンはありません。
- 長い本文は `ScrollViewer` に入れます。

`InfoNote` は `winui/InfoNote.xaml` です。見出しに説明を付ける既存の使い方は、oto調整の「オーバーラップ」です。

## 音源の各種設定

id は `bank`。ロジックは `VoicebankText` と `VoicebankImage`、画面は `MainPage.Bank.cs` です。

- 文字入力は 400ms 後に `character.txt`、`readme.txt`、`character.yaml` へ保存します。アイコンと立ち絵はファイルを選んだ直後に保存します。フォルダを変える前と、`Unloaded` / `Closed` で `FlushBank` します。失敗したときだけログに出します。
- `character.txt` と `readme.txt` の既定は CP932 です。`character.yaml` と、同梱の辞書 yaml は UTF-8 です。`text_file_encoding` はファイルの内容から決め、ユーザーには選ばせません。`oto.ini` か `prefix.map` が shift_jis のとき、`character.txt` に書けない文字があっても utf-8 へは上げません。
- `name`、`image`、`author`、`web` は character.txt と yaml の欄が互いに揃います。`singer_type` は保存時に `utau` です。`voice` と `version` は yaml だけ、`sample` は character.txt だけです。
- yaml の未知キー（`subbanks` など）は元の行のまま残します。画面が触ったキーだけを書き直します。
- アイコンは音源フォルダ直下の 100×100 `icon.bmp` です。余白は白で、切り取らずに収めます。
- 立ち絵の高さ 0 は、小さい画像を 800px まで拡大しません。画素の高さが 800 を超えるときだけ 800 にします。プレビューはその高さと不透明度に合わせ、高さ 800 のときの幅が枠に収まる比率で縮小します。
- プロフィールとローカライズ名は最低 1 行です。1 行のときは削除ボタンを隠し、2 行以上のときだけ出します。空の行はファイルに書きません。
- プレビュー画像はメモリ上のストリームで表示し、ファイルをロックしません。

## 解析と oto.ini

- `oto.ini` は CP932 です。Python 側の定数は `oto.py` の `ANSI` です。各数値の意味は `README.md` の「oto.ini 共通」が根拠です。
- 画面上の調整はセッションに保存します。フォルダを変える前に `FlushSession` します。
- 手で直した音素には `*` が付き、スライダーを動かしてもその値は変わりません。

## 環境設定

アプリ全体の設定は、編集画面のパネルにしない。別ウィンドウ `SettingsWindow`（`winui/SettingsWindow.xaml`）です。

- タイトルバーの「編集」→「環境設定」。`MainPage.OpenSettings_Click` が `SettingsWindow.Show` を呼ぶ。開いているときは前面へ出すだけで、ウィンドウは増やさない。
- メインウィンドウを閉じると、`App` が `CloseIfOpen` する。
- 左は `NavigationView` の見出し。右は、選んだ見出しと同じ文言と、その中身。中身の置き場は `SectionHost`。長い中身はウィンドウ側の `ScrollViewer` がスクロールする。
- 足すときは `SettingsWindow.Sections` で `SettingsSection` を返す。第1引数が左の見出し、第2引数が右の画面。並びの先頭が、開いたときに選ばれる。
- 初期サイズは 960×640、最小は 720×480。メインウィンドウの中央に出す。

「表示」は `DisplaySettings` です。テーマはライト、ダーク、端末に合わせる。アクセントカラーは端末に合わせるか、カラーピッカーで指定します。保存先は `%LocalAppData%\Otoasobi\appearance.json` で、読み書きは `AppAppearanceStore`、起動時の適用は `AppearanceTheme` です。明暗を明示したときは、`App` のコンストラクタで `InitializeComponent` より前に `Application.RequestedTheme` を合わせます。ここで合わせないと、テーマ辞書の文字色が端末の明暗のまま残り、明るい面に白い文字が出ます。各ウィンドウの `RequestedTheme` も合わせ、`MainWindow` はページを作る前に適用します。アクセントは既存のアクセント用ブラシの色だけを差し替え、ブラシのインスタンスは替えません。端末の色へ戻すときは、差し替えた色を外します。

アクセントボタンの地は、明るい面では白文字が読める濃さ、暗い面では黒文字が読める明るさにします。文字色は明るい面で白、暗い面で黒です。無効時の文字は、薄い地に白が乗らないよう、明るい面では薄い黒、暗い面では薄い白にします。タイトルバーのボタンは、明暗を明示したときだけ色を指定し、端末に合わせるときは指定を外します。

波形、スペクトログラム、音量、oto の描画面は明暗に合わせます。区間の種類の色は固定です。コードから色を取るときは `AppearanceTheme.Brush` で、その要素の `ActualTheme` の辞書を見ます。`Application.Current.Resources` はアプリのテーマなので、画面の明暗とは一致しません。テーマが変わったときは `ActualThemeChanged` で塗り直します。

## 確認

```text
dotnet build winui/Otoasobi.csproj
dotnet test tests/OtoParity/OtoParity.csproj
```

オトアソビが起動していると、`Otoasobi.exe` のコピーでビルドが失敗します。そのときはコンパイル結果と、起動中のプロセスを分けて見ます。画面へ反映するには、起動中のアプリを終了してから開き直します。
