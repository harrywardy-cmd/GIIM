import { useState } from 'react'
import { AssetsPage } from './pages/AssetsPage'

type PageKey = 'assets' | 'people' | 'cases' | 'profiles' | 'imports'

const pages: { key: PageKey; label: string; phase: number }[] = [
  { key: 'assets', label: 'Assets', phase: 1 },
  { key: 'people', label: 'People', phase: 1 },
  { key: 'profiles', label: 'Department profiles', phase: 1 },
  { key: 'imports', label: 'Import & reconcile', phase: 1 },
  { key: 'cases', label: 'Onboarding / Offboarding', phase: 2 },
]

export default function App() {
  const [current, setCurrent] = useState<PageKey>('assets')

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
        {current === 'assets' ? (
          <AssetsPage />
        ) : (
          <>
            <h2>{pages.find((p) => p.key === current)?.label}</h2>
            <p className="muted">Not built yet. This page is part of the scaffold.</p>
          </>
        )}
      </main>
    </div>
  )
}
