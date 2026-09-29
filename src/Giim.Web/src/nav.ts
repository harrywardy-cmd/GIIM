import { createContext, useContext } from 'react'

/** Opens things from anywhere in the app, so ticket numbers, names and technicians can be links. */
export type Nav = {
  openAsset: (id: string) => void
  openPerson: (id: string) => void
  openTicket: (ticketNumber: string) => void
  openTechnician: (name: string) => void
}

const noop = () => undefined
export const NavContext = createContext<Nav>({ openAsset: noop, openPerson: noop, openTicket: noop, openTechnician: noop })

export const useNav = () => useContext(NavContext)
