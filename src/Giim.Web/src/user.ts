import { createContext, useContext } from 'react'

export type User = { name: string | null; login: string | null; email: string | null; roles: string[] }

/** What the signed-in user may do. The API enforces the same rules; this only hides buttons they can't use. */
export const permissions = (user: User | null) => {
  const roles = new Set(user?.roles ?? [])
  return {
    canRead: roles.size > 0,
    canChange: roles.has('Technician') || roles.has('Administrator'),
    canAdminister: roles.has('Administrator'),
  }
}

export const UserContext = createContext<User | null>(null)

export const useUser = () => {
  const user = useContext(UserContext)
  return { user, ...permissions(user) }
}

export const initials = (name: string | null | undefined) =>
  (name ?? '?')
    .replace(/@.*/, '')
    .split(/[\s._-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join('') || '?'
