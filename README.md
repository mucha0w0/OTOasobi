# 音遊びラベル

WAV のファイル名（読み仮名）から音素区間を推定し、ラベルテキストを書き出すツールです。波形とメルスペクトログラムの上に区間を重ねて確認できます。

## できること

- ファイル名をモーラに分解し、子音と母音に分けて時間を割り当てる
- 先頭の息、休符、無音を区別してラベルにする
- GUI で区間を確認し、同名の `.txt` を保存する
- 高精度エンジン（MMS 強制アライメントと Praat）があるときはそちらを使い、無いときは音響特徴だけで解析する

## 必要環境

- Windows
- Python 3.12（exe のビルドも 3.12 を想定）

実行時の依存は `requirements.txt` です。

- numpy
- soundfile
- PySide6
- pyqtgraph

高精度エンジンは任意です。`requirements-precision.txt` の `praat-parselmouth`、`torch`、`torchaudio` が入っているときだけ使います。exe には含めません。

## セットアップ

```powershell
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

高精度エンジンも使う場合:

```powershell
.\.venv\Scripts\python.exe -m pip install -r requirements-precision.txt
```

## 起動

GUI:

```powershell
.\.venv\Scripts\python.exe app.py
```

WAV を引数に渡すと、起動と同時にそのファイルを解析します。

```powershell
.\.venv\Scripts\python.exe app.py そせす.wav
```

ウィンドウへ WAV をドロップしても開けます。

| 操作 | ショートカット |
| --- | --- |
| 開く | Ctrl+O |
| 再生 / 停止 | Space |
| 保存 | Ctrl+S |
| 再解析 | F5 |

解析が終わると、WAV と同じ場所に同名の `.txt` を自動で書きます。「保存」から別のパスにも書けます。表の行を選ぶと、波形がその区間へ寄ります。

コマンドラインだけ使う場合:

```powershell
.\.venv\Scripts\python.exe -m otoasobi.cli そせす.wav
```

標準出力にラベル、標準エラーに使ったエンジンと保存先を出します。

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

画面上の色は次のとおりです。

| 種類 | ラベル |
| --- | --- |
| 無音 | `無音` |
| 息 | `息`（先頭の吸気だけ） |
| 子音 | `s` や `k` など |
| 母音 | `a` `i` `u` `e` `o` |

## 解析エンジン

ステータスバーに、使ったエンジンが出ます。

| 表示 | 内容 |
| --- | --- |
| 高精度 (MMS+Praat) | torchaudio の MMS-FA で文字を合わせ、Praat で発話区間・声帯振動・フォルマントを補正する |
| 高精度 (MMS) | MMS-FA のみ。Praat が無い、または使えないとき |
| 音響 | RMS、高域比、ゼロ交差、スペクトル変化だけで区間を切る。exe は常にこちら |

高精度の読み込みに失敗したときは音響へ戻ります。GUI 起動時に高精度モデルを先に読みます。

## exe のビルド

アイコンが無いときは先に作ります。Pillow が必要です。

```powershell
.\.venv\Scripts\python.exe packaging\make_icon.py
powershell -File scripts\build_exe.ps1
```

成果物は `dist\OtoasobiLabel\OtoasobiLabel.exe` です。高精度用のパッケージは同梱しません。

## 構成

| パス | 役割 |
| --- | --- |
| `app.py` | GUI の起動口 |
| `otoasobi/gui.py` | ウィンドウ、再生、保存 |
| `otoasobi/cli.py` | `python -m otoasobi.cli` |
| `otoasobi/parser.py` | ファイル名をモーラへ分解 |
| `otoasobi/analyze.py` | WAV 読み込みとラベル書き出し |
| `otoasobi/segment.py` | 音響エンジンの区間推定 |
| `otoasobi/precision.py` | MMS と Praat を組み合わせた区間推定 |
| `otoasobi/features.py` | フレーム特徴とメルスペクトログラム |
| `otoasobi/breath.py` | 先頭の息 |
| `packaging/otoasobi.spec` | PyInstaller 設定 |
| `scripts/build_exe.ps1` | exe ビルド |
