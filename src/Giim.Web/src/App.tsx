import { useRef, useState, type ReactNode } from 'react'
import {
  Boxes,
  ClipboardList,
  FileText,
  LayoutDashboard,
  Laptop,
  Package,
  RefreshCw,
  ScanLine,
  Search,
  Tags,
  Upload,
  Users,
} from 'lucide-react'
import { api } from './api'
import { assetIdFromLink } from './links'
import { PageHeader } from './PageHeader'
import { AssetDetailsPage } from './pages/AssetDetailsPage'
import { AssetsPage } from './pages/AssetsPage'
import { CategoriesPage } from './pages/CategoriesPage'
import { DashboardPage } from './pages/DashboardPage'
import { ImportPage } from './pages/ImportPage'
import { PeoplePage } from './pages/PeoplePage'
import { ReconciliationPage } from './pages/ReconciliationPage'
import { StockPage } from './pages/StockPage'

export type PageKey =
  | 'dashboard'
  | 'assets'
  | 'stock'
  | 'reconciliation'
  | 'people'
  | 'requests'
  | 'cases'
  | 'imports'
  | 'categories'

type NavItem = { key: PageKey; label: string; icon: ReactNode; later?: string }

const navigation: { section: string; items: NavItem[] }[] = [
  { section: 'Overview', items: [{ key: 'dashboard', label: 'Dashboard', icon: <LayoutDashboard size={18} /> }] },
  {
    section: 'Equipment',
    items: [
      { key: 'assets', label: 'Assets', icon: <Laptop size={18} /> },
      { key: 'stock', label: 'Stock', icon: <Package size={18} /> },
      { key: 'reconciliation', label: 'Intune reconciliation', icon: <RefreshCw size={18} /> },
    ],
  },
  {
    section: 'People',
    items: [
      { key: 'people', label: 'People', icon: <Users size={18} /> },
      { key: 'requests', label: 'Requests', icon: <FileText size={18} />, later: 'Phase 2' },
      { key: 'cases', label: 'On/offboarding', icon: <ClipboardList size={18} />, later: 'Phase 2' },
    ],
  },
  {
    section: 'Setup',
    items: [
      { key: 'imports', label: 'Import register', icon: <Upload size={18} /> },
      { key: 'categories', label: 'Asset categories', icon: <Tags size={18} /> },
    ],
  },
]

export default function App() {
  const [page, setPage] = useState<PageKey>('dashboard')
  // A QR label or shared link opens the asset straight away.
  const [openAsset, setOpenAssetState] = useState<string | null>(() => assetIdFromLink(window.location.search))

  const setOpenAsset = (id: string | null) => {
    setOpenAssetState(id)
    window.history.replaceState(null, '', id ? `?asset=${id}` : window.location.pathname)
  }
  const [search, setSearch] = useState('')
  const [assetSearch, setAssetSearch] = useState('')
  const searchRef = useRef<HTMLInputElement>(null)

  const go = (key: PageKey) => {
    setOpenAsset(null)
    setAssetSearch('')
    setPage(key)
  }

  /** Enter in the top search: an exact serial or asset tag opens the device; anything else searches the asset list. */
  const runSearch = async () => {
    const term = search.trim()
    if (!term) return
    // A 2D scanner reading a GIIM QR label types the whole link.
    const linked = assetIdFromLink(term)
    if (linked) {
      setOpenAsset(linked)
      setSearch('')
      return
    }
    try {
      const found = await api<{ id: string }>(`/api/assets/lookup?code=${encodeURIComponent(term)}`)
      setOpenAsset(found.id)
    } catch {
      setOpenAsset(null)
      setAssetSearch(term)
      setPage('assets')
    }
    setSearch('')
  }

  const label = navigation.flatMap((n) => n.items).find((i) => i.key === page)?.label ?? ''

  const pages: Record<PageKey, ReactNode> = {
    dashboard: <DashboardPage onOpenAsset={setOpenAsset} onNavigate={go} />,
    assets: <AssetsPage key={assetSearch} initialSearch={assetSearch} />,
    stock: <StockPage />,
    reconciliation: <ReconciliationPage />,
    people: <PeoplePage />,
    requests: <PageHeader title={label} subtitle="Device requests and approvals arrive in Phase 2 of the roadmap." />,
    cases: <PageHeader title={label} subtitle="Onboarding and offboarding checklists arrive in Phase 2 of the roadmap." />,
    imports: <ImportPage />,
    categories: <CategoriesPage />,
  }

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark">
            <Boxes size={18} />
          </span>
          GIIM
        </div>
        <nav aria-label="Main">
          {navigation.map((group) => (
            <div key={group.section}>
              <div className="nav-section">{group.section}</div>
              {group.items.map((item) => (
                <button
                  key={item.key}
                  className="nav-item"
                  aria-current={page === item.key && !openAsset ? 'page' : undefined}
                  onClick={() => go(item.key)}
                >
                  {item.icon}
                  {item.label}
                  {item.later && <span className="nav-badge">{item.later}</span>}
                </button>
              ))}
            </div>
          ))}
        </nav>
        <div className="sidebar-user">
          <span className="avatar">LD</span>
          <div>
            <div className="name">local-dev</div>
            <div className="role">IT Technician · sign-in not set up</div>
          </div>
        </div>
      </aside>

      <div className="content">
        <header className="topbar">
          <div className="global-search">
            <Search size={16} />
            <input
              ref={searchRef}
              type="search"
              aria-label="Search assets"
              placeholder="Search or scan a serial, asset tag, model or person, then press Enter"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') runSearch()
              }}
            />
          </div>
          <div className="topbar-actions">
            <button className="icon-button" title="Scan a barcode" aria-label="Scan a barcode" onClick={() => searchRef.current?.focus()}>
              <ScanLine size={18} />
            </button>
            <span className="avatar" title="local-dev">
              LD
            </span>
          </div>
        </header>
        <main>{openAsset ? <AssetDetailsPage assetId={openAsset} onBack={() => setOpenAsset(null)} /> : pages[page]}</main>
      </div>
    </div>
  )
}
