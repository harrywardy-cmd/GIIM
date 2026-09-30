const assetIdPattern = /[?&]asset=([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i

/** The asset in a GIIM link, e.g. a scanned QR label: "https://giim.example/?asset=<id>". */
export const assetIdFromLink = (text: string) => assetIdPattern.exec(text)?.[1] ?? null

const requestIdPattern = /[?&]request=([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i

/** The device request in a GIIM link, e.g. from an approval email: "https://giim.example/?request=<id>". */
export const requestIdFromLink = (text: string) => requestIdPattern.exec(text)?.[1] ?? null
