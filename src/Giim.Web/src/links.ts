const assetIdPattern = /[?&]asset=([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i

/** The asset in a GIIM link, e.g. a scanned QR label: "https://giim.example/?asset=<id>". */
export const assetIdFromLink = (text: string) => assetIdPattern.exec(text)?.[1] ?? null
