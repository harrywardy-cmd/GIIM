import { useState, type ReactNode } from 'react'
import { AssetsPage } from './pages/AssetsPage'
import { CategoriesPage } from './pages/CategoriesPage'
import { ImportPage } from './pages/ImportPage'
import { StockPage } from './pages/StockPage'

type PageKey = 'assets' | 'stock' | 'people' | 'cases' | 'profiles' | 'imports' | 'categories'

const pages: { key: PageKey; label: string; phase: number; page?: ReactNode }[] = [
  { key: 'assets', label: 'Assets', phase: 1, page: <AssetsPage /> },
  { key: 'stock', label: 'Stock', phase: 1, page: <StockPage /> },
  { key: 'people', label: 'People', phase: 1 },
  { key: 'profiles', label: 'Department profiles', phase: 1 },
  { key: 'imports', label: 'Import & reconcile', phase: 1, page: <ImportPage /> },
  { key: 'categories', label: 'Asset categories', phase: 1, page: <CategoriesPage /> },
  { key: 'cases', label: 'Onboarding / Offboarding', phase: 2 },
]

export default function App() {
  const [current, setCurrent] = useState<PageKey>('assets')
  const page = pages.find((p) => p.key === current)!

  return (
    <div className="shell">
      <nav className="nav">
        <h1>GIIM</h1>
        {pages.map((p) => (
          <button
            key={p.key}
            aria-current={current === p.key ? 'page' : undefined}
            onClick={() => setCurrent(p.key)}
          >
            {p.label}
            {p.phase > 1 && <span className="phase"> · phase {p.phase}</span>}
          </button>
        ))}
      </nav>
      <main>
        {page.page ?? (
          <>
            <h2>{page.label}</h2>
            <p className="muted">Not built yet.</p>
          </>
        )}
      </main>
    </div>
  )
}
