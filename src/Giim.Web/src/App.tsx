import { useEffect, useRef, useState, type ReactNode } from 'react'
import {
  Boxes,
  ChartColumn,
  ClipboardList,
  FileText,
  LayoutDashboard,
  LogOut,
  MapPin,
  Laptop,
  Package,
  RefreshCw,
  ScanLine,
  Search,
  Tags,
  Ticket,
  Upload,
  Users,
} from 'lucide-react'
import { api, signedOutEvent } from './api'
import { NoAccessPage, SignInPage } from './pages/SignInPage'
import { initials, permissions, UserContext, type User } from './user'
import { assetIdFromLink } from './links'
import { NavContext, type Nav } from './nav'
import { PageHeader } from './PageHeader'
import { ActivityFeed } from './pages/ActivityFeed'
import { AssetDetailsPage } from './pages/AssetDetailsPage'
import { AssetsPage } from './pages/AssetsPage'
import { CategoriesPage } from './pages/CategoriesPage'
import { DashboardPage } from './pages/DashboardPage'
import { ImportPage } from './pages/ImportPage'
import { LocationsPage } from './pages/LocationsPage'
import { PeoplePage } from './pages/PeoplePage'
import { PersonProfile } from './pages/PersonProfile'
import { ReconciliationPage } from './pages/ReconciliationPage'
import { ReportsPage } from './pages/ReportsPage'
import { SearchResults, type SearchResponse } from './pages/SearchResults'
import { StockPage } from './pages/StockPage'
import { TicketsPage } from './pages/TicketsPage'

export type PageKey =
  | 'dashboard'
  | 'reports'
  | 'assets'
  | 'stock'
  | 'reconciliation'
  | 'tickets'
  | 'people'
  | 'requests'
  | 'cases'
  | 'imports'
  | 'categories'
  | 'locations'

type NavItem = { key: PageKey; label: string; icon: ReactNode; later?: string; adminOnly?: boolean }

const navigation: { section: string; items: NavItem[] }[] = [
  {
    section: 'Overview',
    items: [
      { key: 'dashboard', label: 'Dashboard', icon: <LayoutDashboard size={18} /> },
      { key: 'reports', label: 'Reports', icon: <ChartColumn size={18} /> },
    ],
  },
  {
    section: 'Equipment',
    items: [
      { key: 'assets', label: 'Assets', icon: <Laptop size={18} /> },
      { key: 'stock', label: 'Stock', icon: <Package size={18} /> },
      { key: 'tickets', label: 'Tickets', icon: <Ticket size={18} /> },
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
      { key: 'imports', label: 'Import register', icon: <Upload size={18} />, adminOnly: true },
      { key: 'categories', label: 'Asset categories', icon: <Tags size={18} /> },
      { key: 'locations', label: 'Locations', icon: <MapPin size={18} /> },
    ],
  },
]

/** Something opened on top of the current page; Back returns to what was open before. */
type View =
  | { kind: 'asset'; id: string }
  | { kind: 'person'; id: string }
  | { kind: 'ticket'; number: string }
  | { kind: 'technician'; name: string }
  | { kind: 'search'; term: string }

/** Checks who is signed in, then shows the sign-in page, a no-access page, or GIIM itself. */
export default function App() {
  const [user, setUser] = useState<User | null | undefined>(undefined)
  const [check, setCheck] = useState(0)

  useEffect(() => {
    fetch('/api/me')
      .then((r) => (r.ok ? r.json() : null))
      .then(setUser)
      .catch(() => setUser(null))
  }, [check])

  // Any API call that finds the session has expired sends the user back to sign in.
  useEffect(() => {
    const signedOut = () => setUser(null)
    window.addEventListener(signedOutEvent, signedOut)
    return () => window.removeEventListener(signedOutEvent, signedOut)
  }, [])

  if (user === undefined) return <p className="muted" style={{ padding: 32 }}>Loading…</p>
  if (user === null) return <SignInPage onSignedIn={() => setCheck((n) => n + 1)} />
  if (user.roles.length === 0) return <NoAccessPage name={user.name} />

  return (
    <UserContext.Provider value={user}>
      <Shell user={user} />
    </UserContext.Provider>
  )
}

function Shell({ user }: { user: User }) {
  const { canAdminister } = permissions(user)
  const [page, setPage] = useState<PageKey>('dashboard')
  // A QR label or shared link opens the asset straight away.
  const [views, setViews] = useState<View[]>(() => {
    const linked = assetIdFromLink(window.location.search)
    return linked ? [{ kind: 'asset', id: linked }] : []
  })
  const [search, setSearch] = useState('')
  const [assetSearch, setAssetSearch] = useState('')
  const searchRef = useRef<HTMLInputElement>(null)
  const current = views.at(-1)

  const show = (next: View[]) => {
    setViews(next)
    const top = next.at(-1)
    window.history.replaceState(null, '', top?.kind === 'asset' ? `?asset=${top.id}` : window.location.pathname)
  }
  const open = (view: View) => show([...views, view])
  const back = () => show(views.slice(0, -1))

  const nav: Nav = {
    openAsset: (id) => open({ kind: 'asset', id }),
    openPerson: (id) => open({ kind: 'person', id }),
    openTicket: (number) => open({ kind: 'ticket', number }),
    openTechnician: (name) => open({ kind: 'technician', name }),
  }

  const go = (key: PageKey) => {
    show([])
    setAssetSearch('')
    setPage(key)
  }

  /**
   * Enter in the top search: a QR link, exact serial/asset tag or exact ticket number opens it directly;
   * anything else shows grouped results (assets, people, tickets, technicians).
   */
  const runSearch = async () => {
    const term = search.trim()
    if (!term) return
    setSearch('')
    const linked = assetIdFromLink(term)
    if (linked) return open({ kind: 'asset', id: linked })

    try {
      const result = await api<SearchResponse>(`/api/search?q=${encodeURIComponent(term)}`)
      if (result.exactAssetId) return open({ kind: 'asset', id: result.exactAssetId })
      if (result.exactTicket) return open({ kind: 'ticket', number: result.exactTicket })
    } catch {
      // Fall through to the results page, which shows the error.
    }
    open({ kind: 'search', term })
  }

  const label = navigation.flatMap((n) => n.items).find((i) => i.key === page)?.label ?? ''

  const pages: Record<PageKey, ReactNode> = {
    dashboard: <DashboardPage onOpenAsset={nav.openAsset} onNavigate={go} />,
    reports: <ReportsPage />,
    assets: <AssetsPage key={assetSearch} initialSearch={assetSearch} />,
    stock: <StockPage />,
    reconciliation: <ReconciliationPage />,
    tickets: <TicketsPage />,
    people: <PeoplePage />,
    requests: <PageHeader title={label} subtitle="Device requests and approvals arrive in Phase 2 of the roadmap." />,
    cases: <PageHeader title={label} subtitle="Onboarding and offboarding checklists arrive in Phase 2 of the roadmap." />,
    imports: <ImportPage />,
    categories: <CategoriesPage />,
    locations: <LocationsPage />,
  }

  const overlay = !current ? null : current.kind === 'asset' ? (
    <AssetDetailsPage key={current.id} assetId={current.id} onBack={back} />
  ) : current.kind === 'person' ? (
    <PersonProfile key={current.id} personId={current.id} onBack={back} onOpenPerson={nav.openPerson} />
  ) : current.kind === 'ticket' ? (
    <ActivityFeed key={current.number} ticket={current.number} onBack={back} />
  ) : current.kind === 'technician' ? (
    <ActivityFeed key={current.name} technician={current.name} onBack={back} />
  ) : (
    <SearchResults
      key={current.term}
      term={current.term}
      onSeeAllAssets={(term) => {
        show([])
        setAssetSearch(term)
        setPage('assets')
      }}
    />
  )

  return (
    <NavContext.Provider value={nav}>
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
                {group.items.filter((item) => !item.adminOnly || canAdminister).map((item) => (
                  <button
                    key={item.key}
                    className="nav-item"
                    aria-current={page === item.key && !current ? 'page' : undefined}
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
            <span className="avatar">{initials(user.name)}</span>
            <div>
              <div className="name">{user.name}</div>
              <div className="role">{user.roles.join(', ')}</div>
              <form method="post" action="/auth/logout">
                <button type="submit" className="sidebar-signout">
                  <LogOut size={14} /> Sign out
                </button>
              </form>
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
                aria-label="Search everything"
                placeholder="Search or scan: serial, asset tag, person, ticket, model or technician, then press Enter"
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
              <span className="avatar" title={`${user.name} (${user.roles.join(', ')})`}>
                {initials(user.name)}
              </span>
            </div>
          </header>
          <main>
            {current && current.kind === 'search' && views.length > 1 && (
              <button onClick={back} style={{ marginTop: 0, marginBottom: 12 }}>
                ← Back
              </button>
            )}
            {overlay ?? pages[page]}
          </main>
        </div>
      </div>
    </NavContext.Provider>
  )
}
