import { useEffect, useState } from 'react'
import { Boxes, LogIn } from 'lucide-react'

type Config = { mode: 'Okta' | 'Development'; roles: string[] }

/**
 * Shown when nobody is signed in. With Okta, the button hands over to Okta (MFA and all) and comes back here.
 * The development sign-in only exists on a developer's machine; the API refuses it anywhere else.
 */
export function SignInPage({ onSignedIn }: { onSignedIn: () => void }) {
  const [config, setConfig] = useState<Config | null>(null)
  const [name, setName] = useState('john.smith')
  const [role, setRole] = useState('Technician')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    fetch('/api/auth/config')
      .then((r) => r.json())
      .then(setConfig)
      .catch(() => setError('Could not reach GIIM. Is the API running?'))
  }, [])

  const devSignIn = async () => {
    const response = await fetch('/auth/dev-login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, role }),
    })
    if (response.ok) onSignedIn()
    else setError((await response.json().catch(() => null))?.detail ?? 'Sign-in failed.')
  }

  const returnUrl = encodeURIComponent(window.location.pathname + window.location.search)

  return (
    <div className="signin">
      <div className="signin-card">
        <div className="brand signin-brand">
          <span className="brand-mark">
            <Boxes size={18} />
          </span>
          GIIM
        </div>
        <h2>Sign in</h2>
        <p className="muted">IT asset and lifecycle management.</p>
        {error && <p className="error">{error}</p>}

        {config?.mode === 'Okta' && (
          <a className="button primary signin-button" href={`/auth/login?returnUrl=${returnUrl}`}>
            <LogIn size={16} /> Sign in with Okta
          </a>
        )}

        {config?.mode === 'Development' && (
          <>
            <p className="badge-warn">Development sign-in: this computer only</p>
            <div className="form-row">
              <label className="grow">
                Name
                <input value={name} onChange={(e) => setName(e.target.value)} />
              </label>
              <label>
                Role
                <select value={role} onChange={(e) => setRole(e.target.value)}>
                  {config.roles.map((r) => (
                    <option key={r}>{r}</option>
                  ))}
                </select>
              </label>
            </div>
            <button className="primary" onClick={devSignIn} disabled={!name.trim()}>
              <LogIn size={16} /> Sign in
            </button>
          </>
        )}
      </div>
    </div>
  )
}

/** Signed in with Okta, but not in any GIIM group. */
export function NoAccessPage({ name }: { name: string | null }) {
  return (
    <div className="signin">
      <div className="signin-card">
        <h2>No access to GIIM</h2>
        <p>
          You're signed in{name ? ` as ${name}` : ''}, but you're not in any GIIM group. Ask the IT service desk to add you to
          the right group (Viewer, Technician, Manager or Administrator).
        </p>
        <form method="post" action="/auth/logout">
          <button type="submit">Sign out</button>
        </form>
      </div>
    </div>
  )
}
