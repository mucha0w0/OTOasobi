import { otoField } from "./otoFields"
import type { OtoMark } from "./protocol"

const headers = ["エイリアス", "オフセット", "オーバーラップ", "先行発声", "子音部", "ブランク"]

export function OtoTable({ marks }: { marks: OtoMark[] }) {
  if (marks.length === 0)
    return <p className="p-3 text-sm text-[var(--muted)]">解析が終わると、計算した oto.ini の数値がここに出ます。</p>
  return (
    <div className="h-full overflow-auto">
      <table className="w-full border-collapse text-left text-sm">
        <thead>
          <tr>
            {headers.map((header) => (
              <th key={header} className="sticky top-0 border-b border-[var(--line)] bg-[var(--panel)] px-2 py-2 font-medium">{header}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {marks.map((mark) => (
            <tr key={mark.alias}>
              <td className="border-b border-[var(--line)] px-2 py-1">{mark.alias}</td>
              {[0, 1, 2, 3, 4].map((point) => (
                <td key={point} className="border-b border-[var(--line)] px-2 py-1 tabular-nums">{otoField(mark, point).toFixed(3)}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
