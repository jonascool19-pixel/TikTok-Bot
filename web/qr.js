export function tikTokLink(text) {
  let url;
  try { url = new URL(text); } catch {}
  if (url?.protocol !== 'https:' || url.username || url.password || url.port ||
      !(url.hostname === 'tiktok.com' || url.hostname.endsWith('.tiktok.com'))) {
    throw new Error('Der QR-Code enthält keinen HTTPS-Link zu TikTok.');
  }
  return url.href;
}

export async function decodeTikTokQr(file) {
  if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 10 * 1024 * 1024) {
    throw new Error('Wähle einen Screenshot als PNG, JPEG oder WebP bis 10 MB.');
  }
  await import('/jsqr.js');
  const image = await createImageBitmap(file).catch(() => { throw new Error('Das Bild konnte nicht geöffnet werden.'); });
  try {
    const scale = Math.min(1, 2400 / Math.max(image.width, image.height));
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(image.width * scale));
    canvas.height = Math.max(1, Math.round(image.height * scale));
    const context = canvas.getContext('2d', { willReadFrequently: true });
    context.drawImage(image, 0, 0, canvas.width, canvas.height);
    const pixels = context.getImageData(0, 0, canvas.width, canvas.height);
    const result = globalThis.jsQR(pixels.data, pixels.width, pixels.height);
    if (!result) throw new Error('Kein QR-Code erkannt. Schneide den Screenshot näher am Code zu und versuche es erneut.');
    return tikTokLink(result.data);
  } finally { image.close(); }
}
