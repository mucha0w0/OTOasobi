# OTOasobi

オトアソビ。WAV のファイル名（読み仮名）から音素区間を推定し、同じ場所にラベルテキストを書き出す Windows アプリです。

画面は WinUI 3、解析は Python です。

## できること

- WAV を選ぶか、ウィンドウへドロップして解析する
- ファイル名をモーラに分解し、子音と母音に分けて時間を割り当てる
- 先頭の息、休符、無音を区別してラベルにする
- 解析結果を、WAV と同じ場所の同名 `.txt` に保存する
- 高精度エンジン（MMS 強制アライメントと Praat）があるときはそちらを使い、無いときは音響特徴だけで解析する

## 必要環境

- Windows
- .NET 10 SDK（画面）
- Python 3.12（解析）

解析の依存は `requirements.txt` です。

- numpy
- soundfile

高精度エンジンは `requirements-precision.txt` の `praat-parselmouth`、`torch`、`torchaudio` です。`.venv` に入っているとき、OTOasobi の解析でも使います。

## セットアップ

```powershell
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe -m pip install -r requirements-precision.txt
```

.NET 10 SDK は [ダウンロードページ](https://dotnet.microsoft.com/download) から入れます。入っているかは `dotnet --version` で確認できます。

## 起動

```powershell
dotnet run --project winui
```

起動すると「ファイルを選択またはドラッグ＆ドロップ」と出ます。WAV を選ぶかドロップすると解析が始まり、完了すると同じ場所に `.txt` を保存します。「txtファイルを開く」でそのファイルを開きます。

ファイル名に使えない文字があるときは、その理由を表示します。

## ファイル名

拡張子を除いた名前が読み仮名です。使える文字はひらがな、カタカナ（`ヴ` を含む外来音）、休符 `_` だけです。半角カナの `ｳﾞ` や、濁点を離した `ウ゛` も `ヴ` として読みます。

| 記法 | 意味 |
| --- | --- |
| `そ` `せ` `しゃ` `つ` | 1 モーラ。拗音・外来音は 2 文字で 1 モーラ |
| `_` | 休符。連続した `_` は 1 つの無音 |
| 先頭の `_` | 発話前の無音 |
| 末尾の `_` | 発話後の無音 |
| 途中の `_` | モーラのあいだの無音 |

例: `_そせすそそ.wav` は、無音、そ、せ、す、そ、そ、無音、の順です。

`ん` の直後がま行・ば行・ぱ行のときはラベルが `M` になります。`っ` は閉鎖 `cl` です。同じ母音が続く `ああ` や `かあ` は、いったん 1 区間として合わせてから、元のモーラ数で等分します。

使えない文字や、単独の `ゃゅょ` はファイル名エラーになります。

## ラベルファイル

タブ区切り、UTF-8、1 行が 1 区間です。

```text
開始秒	終了秒	音素	かな
```

`_そせすそそ.wav` の例:

```text
0.000000	0.160000	無音	_
0.160000	0.238390	s	そ
0.238390	0.652494	o	そ
0.652494	0.732381	s	せ
0.732381	1.162494	e	せ
```

音素はローマ字です。母音は `a i u e o`、子音は `s` `sh` `k` `ts` `ch` など、撥音は `N` または `M`、促音は `cl` です。かな列は、その区間が属すモーラの表記です。

| 種類 | ラベル |
| --- | --- |
| 無音 | `無音` |
| 息 | `息`（先頭の吸気だけ） |
| 子音 | `s` や `k` など |
| 母音 | `a` `i` `u` `e` `o` |

## 解析エンジン

| エンジン | 内容 |
| --- | --- |
| 高精度 (MMS+Praat) | torchaudio の MMS-FA で文字を合わせ、Praat で発話区間・声帯振動・フォルマントを補正する |
| 高精度 (MMS) | MMS-FA のみ。Praat が無い、または使えないとき |
| 音響 | RMS、高域比、ゼロ交差、スペクトル変化だけで区間を切る |

高精度の読み込みに失敗したときは音響へ戻ります。

## 開発用の確認画面

`app.py` は解析結果を波形とメルスペクトログラムで見るための開発用画面です。OTOasobi には組み込みません。

```powershell
.\.venv\Scripts\python.exe app.py
.\.venv\Scripts\python.exe -m otoasobi.cli そせす.wav
```

開発用画面の exe は `powershell -File scripts\build_exe.ps1` で、`dist\OtoasobiLabel\OtoasobiLabel.exe` に出力されます。高精度エンジンは同梱しません。

## 構成

| パス | 役割 |
| --- | --- |
| `winui/` | OTOasobi の画面（WinUI 3） |
| `otoasobi/worker.py` | 画面から呼ばれる解析プロセス |
| `otoasobi/analyze.py` | WAV 読み込みとラベル書き出し |
| `otoasobi/parser.py` | ファイル名をモーラへ分解 |
| `otoasobi/segment.py` | 音響エンジンの区間推定 |
| `otoasobi/precision.py` | MMS と Praat を組み合わせた区間推定 |
| `otoasobi/features.py` | フレーム特徴とメルスペクトログラム |
| `otoasobi/breath.py` | 先頭の息 |
| `app.py` | 開発用の確認画面 |
| `otoasobi/gui.py` | 開発用画面のウィンドウ |
| `otoasobi/cli.py` | 開発用のコマンドライン |
