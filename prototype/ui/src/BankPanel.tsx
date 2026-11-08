import { useEffect, useRef, useState } from "react"
import type { BankEvent, BankLocale, BankRow, PageCommand } from "./protocol"

export function BankPanel({
  bank,
  send,
}: {
  bank: BankEvent | null
  send: (command: PageCommand) => void
}) {
  const [form, setForm] = useState<BankEvent | null>(bank)
  const formRef = useRef(form)
  const sendRef = useRef(send)
  const timer = useRef<number | undefined>(undefined)
  formRef.current = form
  sendRef.current = send

  useEffect(() => () => {
    window.clearTimeout(timer.current)
    const current = formRef.current
    if (!current)
      return
    sendRef.current({
      type: "bank",
      generation: current.generation,
      name: current.name,
      image: current.image,
      sample: current.sample,
      author: current.author,
      web: current.web,
      profile: current.profile,
      extra: current.extra,
      readme: current.readme,
      voice: current.voice,
      version: current.version,
      locales: current.locales,
      portrait: current.portrait,
      opacity: current.opacity,
      height: current.height,
    })
  }, [])

  useEffect(() => {
    setForm(bank)
    window.clearTimeout(timer.current)
  }, [bank])

  function commit(next: BankEvent) {
    formRef.current = next
    setForm(next)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => {
      const current = formRef.current
      if (!current)
        return
      send({
        type: "bank",
        generation: current.generation,
        name: current.name,
        image: current.image,
        sample: current.sample,
        author: current.author,
        web: current.web,
        profile: current.profile,
        extra: current.extra,
        readme: current.readme,
        voice: current.voice,
        version: current.version,
        locales: current.locales,
        portrait: current.portrait,
        opacity: current.opacity,
        height: current.height,
      })
    }, 400)
  }

  function flush(which: "icon" | "portrait") {
    window.clearTimeout(timer.current)
    const current = formRef.current
    if (current) {
      send({
        type: "bank",
        generation: current.generation,
        name: current.name,
        image: current.image,
        sample: current.sample,
        author: current.author,
        web: current.web,
        profile: current.profile,
        extra: current.extra,
        readme: current.readme,
        voice: current.voice,
        version: current.version,
        locales: current.locales,
        portrait: current.portrait,
        opacity: current.opacity,
        height: current.height,
      })
    }
    send({ type: "bankPick", which })
  }

  function patch(partial: Partial<BankEvent>) {
    const current = formRef.current
    if (!current)
      return
    commit({ ...current, ...partial })
  }

  if (!form)
    return <p className="p-3 text-sm text-[var(--muted)]">フォルダを開くと、character.txt と character.yaml がここに出ます。</p>

  const portraitHeight = form.height > 0 ? form.height : undefined
  return (
    <div className="flex h-full flex-col gap-4 overflow-auto p-3 text-sm">
      <section className="grid gap-2">
        <h2 className="section-title">アイコン</h2>
        <p className="mt-1 text-[var(--muted)]">100×100 の icon.bmp として、音源フォルダの直下に保存します。</p>
        {form.icon
          ? <img src={form.icon} alt="アイコン" className="mt-2 h-[100px] w-[100px]" />
          : <p className="mt-2 text-[var(--muted)]">アイコン画像が設定されていません</p>}
        <button type="button" className="ghost-button mt-2 w-full" onClick={() => flush("icon")}>ファイルを選ぶ</button>
      </section>
      <section className="grid gap-2">
        <h2 className="section-title">character.txt</h2>
        <p className="mt-1 text-[var(--muted)]">UTAU の原音プロパティに出る内容です。名前、画像、作者、配布サイトは character.yaml とも揃えます。</p>
        <Field label="名前" value={form.name} onChange={(name) => patch({ name })} />
        <Field label="画像ファイル名" value={form.image} onChange={(image) => patch({ image })} />
        <label className="mt-2 grid gap-1">
          サンプル
          <select className="field" value={form.sample} onChange={(event) => patch({ sample: event.target.value })}>
            <option value="">ランダム</option>
            {form.samples.filter((item) => item !== "ランダム").map((item) => <option key={item}>{item}</option>)}
          </select>
        </label>
        <Field label="作者" value={form.author} onChange={(author) => patch({ author })} />
        <Field label="配布サイト" value={form.web} onChange={(web) => patch({ web })} />
        <p className="mt-3">プロフィール</p>
        {form.profile.map((row, index) => (
          <div key={index} className="mt-2 flex gap-2">
            <input className="field" value={row.label} placeholder="項目" onChange={(event) => patch({ profile: replaceRow(form.profile, index, { ...row, label: event.target.value }) })} />
            <input className="field" value={row.value} placeholder="内容" onChange={(event) => patch({ profile: replaceRow(form.profile, index, { ...row, value: event.target.value }) })} />
            {form.profile.length > 1 && (
              <button type="button" className="ghost-button" onClick={() => patch({ profile: form.profile.filter((_, item) => item !== index) })}>削除</button>
            )}
          </div>
        ))}
        <button type="button" className="ghost-button mt-2" onClick={() => patch({ profile: [...form.profile, { label: "", value: "", fullwidth: true }] })}>行を追加</button>
        <label className="mt-3 grid gap-1">
          その他の行
          <textarea className="field min-h-16" value={form.extra} onChange={(event) => patch({ extra: event.target.value })} />
        </label>
      </section>
      <section className="grid gap-2">
        <h2 className="section-title">readme.txt</h2>
        <textarea className="field mt-2 min-h-28" value={form.readme} onChange={(event) => patch({ readme: event.target.value })} />
      </section>
      <section className="grid gap-2">
        <h2 className="section-title">エンコード</h2>
        <p className="mt-1">text_file_encoding: {form.encoding}</p>
        <p className="mt-1 text-[var(--muted)]">character.txt、prefix.map、oto.ini の内容から決めます。</p>
      </section>
      <section className="grid gap-2">
        <h2 className="section-title">キャラクター情報</h2>
        <p className="mt-1 text-[var(--muted)]">シンガータイプは utau です。声とバージョンは yaml だけに書きます。</p>
        <p className="mt-2">シンガータイプ　utau</p>
        <Field label="声" value={form.voice} onChange={(voice) => patch({ voice })} />
        <Field label="バージョン" value={form.version} onChange={(version) => patch({ version })} />
      </section>
      <section className="grid gap-2">
        <h2 className="section-title">ローカライズ名</h2>
        {form.locales.map((row, index) => (
          <div key={index} className="mt-2 flex gap-2">
            <select className="field" value={row.code} onChange={(event) => patch({ locales: replaceLocale(form.locales, index, { ...row, code: event.target.value }) })}>
              <option value="">言語</option>
              {form.languages.map((item) => <option key={item.code} value={item.code}>{item.label}</option>)}
            </select>
            <input className="field" value={row.name} placeholder="名前" onChange={(event) => patch({ locales: replaceLocale(form.locales, index, { ...row, name: event.target.value }) })} />
            {form.locales.length > 1 && (
              <button type="button" className="ghost-button" onClick={() => patch({ locales: form.locales.filter((_, item) => item !== index) })}>削除</button>
            )}
          </div>
        ))}
        <button type="button" className="ghost-button mt-2" onClick={() => patch({ locales: [...form.locales, { code: "", name: "" }] })}>言語を追加</button>
      </section>
      <section className="grid gap-2">
        <h2 className="section-title">立ち絵</h2>
        <p className="mt-1 text-[var(--muted)]">高さ 0 は既定です。画素の高さが 800 を超えるときだけ、表示を 800 にします。</p>
        {form.portraitImage
          ? <img src={form.portraitImage} alt="立ち絵" className="mt-2 max-w-full" style={{ opacity: form.opacity, maxHeight: portraitHeight && portraitHeight < 800 ? portraitHeight : 320 }} />
          : <p className="mt-2 text-[var(--muted)]">立ち絵が設定されていません</p>}
        <Field label="ファイル" value={form.portrait} onChange={(portrait) => patch({ portrait })} />
        <button type="button" className="ghost-button mt-2 w-full" onClick={() => flush("portrait")}>ファイルを選ぶ</button>
        <label className="mt-2 grid gap-1">
          <span className="flex justify-between">
            不透明度
            <span className="num text-[var(--muted)]">{form.opacity.toFixed(2)}</span>
          </span>
          <input type="range" min={0} max={1} step={0.01} value={form.opacity} onChange={(event) => patch({ opacity: Number(event.target.value) })} />
        </label>
        <Field label="高さ" value={String(form.height)} onChange={(value) => patch({ height: Math.max(0, Number(value) || 0) })} />
      </section>
    </div>
  )
}

function Field({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return (
    <label className="mt-2 grid gap-1">
      {label}
      <input className="field" value={value} onChange={(event) => onChange(event.target.value)} />
    </label>
  )
}

function replaceRow(rows: BankRow[], index: number, row: BankRow) {
  return rows.map((item, itemIndex) => itemIndex === index ? row : item)
}

function replaceLocale(rows: BankLocale[], index: number, row: BankLocale) {
  return rows.map((item, itemIndex) => itemIndex === index ? row : item)
}
