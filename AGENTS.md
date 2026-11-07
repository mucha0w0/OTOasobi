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
| `MainPage.FrqEdit.cs` | frqエディター |
| `MainPage.Bank.cs` | 音源の各種設定 |
| `MainPage.LayoutTemplates.cs` | 配置テンプレート |
| `MainPage.Rec.cs` | 録音リストと録音 |
| `MainPage.Mic.cs` | マイクモニター |

## パネルを足す

編集画面のパネルはタブにも別ウィンドウにもしません。`EditorDock` の葉です。配置の分割はドックが行うので、パネル側で画面を割りません。

1. `EditorLayout.PaneIds` に短い id を足す。ここに無い id は `Show` が無視します。保存済みの `editor-layout.json` に新しい id が無くても、そのファイルは無効になりません。単にそのパネルが出ないだけです。
2. 初期画面に出すかは `CreateDefault()` だけが決めます。頼まれない限りここへは入れません。初期で閉じているのは、エフェクター `source`、oto数値表 `otoParams`、`.frq生成` `frq`、frqエディター `frqEdit`、音源の各種設定 `bank`、録音リスト `recList`、録音 `rec`、マイクモニター `mic` です。配置テンプレートの「録音」は `EditorLayout.CreateRecording` です。
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

## グラフのホイール

音素推定、スペクトログラム、音量、oto設定では、カーソルが乗っている面だけがホイールを受ける。そこでファイルリストの前後へ移す。上へ回すと前のファイル。Shift＋ホイールだけが時間の横スクロール。Ctrl＋ホイールは、ポインタ位置を基準に拡大・縮小する。右ドラッグと左右キーのスクロールはそのまま。ページ全体では受けない。パネルを動かしても、当たりは画面上の位置ではなく、その面に付いたまま。ほかのパネルは、それぞれの縦スクロールのまま。

## frqエディター

id は `frqEdit`。初期配置には入れません。読み書きは `FrqFile`、点の移動と範囲は `FrqPitch`、画面は `FrqPianoRoll`、ファイルとの接続は `MainPage.FrqEdit.cs` です。

- 選択中の WAV の隣の `.frq`（FREQ0003）を読み、ピアノロールに点で出します。高さ 0 は下の段の音程なしです。サンプルレートは WAV のヘッダから取り、目盛りを秒にします。
- カーソルは矩形選択です。選択中だけ ＋12、-12、- を出します。＋12 と -12 は 1 オクターブ（周波数を 2 倍または半分）で、高さ 0 と、C-1 から G9 の外へ出る結果は変えません。- は高さ 0 です。カーソル、ペン、＋12、-12、- はピアノロール上、鍵盤の右に重ね、帯は置きません。左端は `FrqPitch.KeyWidth` に 8 を足します。面は不透明です。
- ペンはドラッグしたフレームの高さを、ポインタの音程に書き換えます。離すとファイルへ書き、振幅と hop はそのまま、平均は有声の平均を書き直します。
- ファイルを変える前に、書きかけを書き出します。生成を始めると、ペンの途中は捨てます。生成が終わると、ディスクの `.frq` を読み直します。

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

## 録音

id は `recList`、`rec`、`mic`。初期配置には入れません。配置テンプレート「録音」が、リスト、録音、マイクモニター、ファイル、音素推定の並びです。ロジックは `RecList` と `RecTone`、マイクは `RecCapture`、画面は `MainPage.Rec.cs` と `MainPage.Mic.cs` です。

- 録音リストは 1 行が 1 テイクです。空行と `#` で始まる行は飛ばし、タブの右側はコメントです。文字コードは CP932 と UTF-8 です。選択はドロップダウンで、先頭が「選択しない」、次が「ファイルから選ぶ」です。「ファイルから選ぶ」でファイルを開きます。一度選んだパスは `%LocalAppData%\Otoasobi\recording-lists.json` に最大 20 件残り、次からこのメニューに出ます。音源フォルダに `録音リスト.txt` があるとき、`OTOasibi_録音.json` のリストが未設定（null）ならそれを開きます。リストが空文字のときは「選択しない」です。選んだリストと、ガイドの WAV、ファイルで鳴らすか自分で設定するかは `OTOasibi_録音.json` です。パネルの上から、見出し、ドロップダウン、録音済みの数とリストの行数、すべて・未録音・録音済み・リスト外です。リスト外は、どの行の `音素.wav` とも一致しないフォルダ内の WAV で、すべてには出しません。
- 保存するファイル名は、行の音素に `.wav` を足したものです。ひらがな、カタカナ、`_` 以外の行は「使えない文字」で、録音しません。
- ガイド音は音程の基準音です。録音パネルの「ファイルを選ぶ」と「自分で設定する」で切り替えます。ファイルを選ぶときはドロップダウンだけで、先頭が「選択しない」、次が「ファイルから選ぶ」です。選ぶのは WAV だけで、同じ名前の txt が対になります。一度選んだ WAV は `%LocalAppData%\Otoasobi\recording-guides.json` に最大 20 件残り、次からこの中に出ます。録音先のフォルダにある WAV は選べません。自分で設定するときは、音程、大きさ、カウント、テンポ、録音中のガイドだけを出します。選んだ方法と WAV のパスは `OTOasibi_録音.json` です。まだ選んでいないときは、自分で設定する、です。
- 保存は 44.1 kHz、16 bit、モノラルです。先頭の無音は切りません。録音のたびに解析はしません。音程、大きさ、カウント、テンポ、録音中のガイド、次の未録音へ進むかは `%LocalAppData%\Otoasobi\recording.json` です。入力デバイスと出力デバイスも同じファイルで、画面は環境設定の「録音」です。
- マイクは録音パネルかマイクモニターが見えているときだけ開きます。フォルダを変える前、解析を始める前、ウィンドウを閉じるときに、録音中のテイクを書き出します。入力を変えると、パネルが見えているときだけマイクを開き直します。出力はガイドとテイクの再生先です。
- マイクモニターは、波形がパネル全体です。未録音の行ではマイクの直近 3 秒、録音済みの行ではその WAV です。右のレベルメーターはマイクのレベルで、0 dB までを目盛りにします。左上の小さなトグルが「入力を聴く」で、マイクを出力へつなぎます。既定はオフです。録音済みの行では、その横に再生ボタンが出ます。再生位置は波形の上です。パネルを閉じると、入力のモニタは止まります。
- WAV の無いフォルダも編集画面で開きます。解析は、ディスクに WAV ができたあとです。

## 解析と oto.ini

- `oto.ini` は CP932 です。Python 側の定数は `oto.py` の `ANSI` です。各数値の意味は `README.md` の「oto.ini 共通」が根拠です。
- 画面上の調整はセッションに保存します。フォルダを変える前に `FlushSession` します。
- 手で直した音素には `*` が付き、スライダーを動かしてもその値は変わりません。

## 環境設定

アプリ全体の設定は、編集画面のパネルにしない。別ウィンドウ `SettingsWindow`（`winui/SettingsWindow.xaml`）です。

- タイトルバーの「編集」→「環境設定」。`MainPage.OpenSettings_Click` が `SettingsWindow.Show` を呼ぶ。開いているときは前面へ出すだけで、ウィンドウは増やさない。
- メインウィンドウの所有者にする（`GWLP_HWNDPARENT`。`SetParent` ではない）。メニューを閉じたときのフォーカス復帰や、メイン側の再描画で前面を取られても背面に回らない。タスクバーのボタンは `WS_EX_APPWINDOW` で残す。表示の直後と、その後の低優先度でもう一度前面にする。
- メインウィンドウを閉じると、`App` が `CloseIfOpen` する。
- 左は `NavigationView` の見出し。右は、選んだ見出しと同じ文言と、その中身。中身の置き場は `SectionHost`。長い中身はウィンドウ側の `ScrollViewer` がスクロールする。
- 足すときは `SettingsWindow.Sections` で `SettingsSection` を返す。第1引数が左の見出し、第2引数が右の画面。並びの先頭が、開いたときに選ばれる。
- 初期サイズは 960×640、最小は 720×480。メインウィンドウの中央に出す。

「表示」は `DisplaySettings` です。テーマはライト、ダーク、端末に合わせる。アクセントカラーは端末に合わせるか、カラーピッカーで指定します。保存先は `%LocalAppData%\Otoasobi\appearance.json` で、読み書きは `AppAppearanceStore`、起動時の適用は `AppearanceTheme` です。明暗を明示したときは、`App` のコンストラクタで `InitializeComponent` より前に `Application.RequestedTheme` を合わせます。ここで合わせないと、テーマ辞書の文字色が端末の明暗のまま残り、明るい面に白い文字が出ます。この値は起動後には変えません。ウィンドウの `RequestedTheme` は、その起動時の明暗と同じにします。ずらすと、地だけが変わって文字色が残り、暗い地に暗い文字が出ます。`ElementTheme.Default` は代入するとダークになるので、端末に合わせるときも、起動した明暗をライトかダークで明示します。明暗を切り替えたときは、設定を保存してから同じ実行ファイルを起動し直し、いまのプロセスを終了します。その前にセッションと音源の入力を書き出します。各ウィンドウはページを作る前に適用します。アクセントは、いまの明暗の辞書にあるブラシの色だけを差し替え、ブラシのインスタンスは替えません。別の明暗の辞書から同じブラシが返ったときは、その明暗の色では塗りません。端末の色へ戻すときは、差し替えた色を外します。

アクセントボタンは、明るい面では白文字が読める濃さにして文字を白にします。暗い面は、以前の明るい段階（`Light2` / `Light3`）のままです。無効なアクセントボタンの文字は、明るい面だけ薄い黒にします。タイトルバーのボタンは、ライトのときだけ色を指定し、ダークと端末に合わせるときは指定を外します。

「録音」は `RecordingSettings` です。入力デバイスと出力デバイスを選びます。入力は録音するマイク、出力はガイドとテイクの再生先です。保存先は `recording.json` で、音程やカウントなど録音パネルの項目と同じファイルです。変えると、開いている録音へすぐ反映します。マイクを開くのは、録音パネルが見えているあいだだけです。

波形、スペクトログラム、音量、oto の描画面は明暗に合わせます。区間の種類の色は固定です。コードから色を取るときは `AppearanceTheme.Brush` で、その要素の `ActualTheme` の辞書を見ます。`Application.Current.Resources` はアプリのテーマなので、画面の明暗とは一致しません。テーマが変わったときは `ActualThemeChanged` で塗り直します。

## 確認

```text
dotnet build winui/Otoasobi.csproj
dotnet test tests/OtoParity/OtoParity.csproj
```

オトアソビが起動していると、`Otoasobi.exe` のコピーでビルドが失敗します。そのときはコンパイル結果と、起動中のプロセスを分けて見ます。画面へ反映するには、起動中のアプリを終了してから開き直します。
