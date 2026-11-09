import type Hls from 'hls.js';

const MAX_NETWORK_RETRIES = 2;
const NETWORK_RETRY_DELAY_MS = 1_500;

export interface HlsCallbacks {
  isCancelled(): boolean;
  /** Поток загружается заново: плееру нужно вернуться на текущее место. */
  onReload(currentTime: number): void;
  onFatal(detail: string): void;
}

/** Подключает HLS к элементу; null означает встроенный HLS браузера или отменённый запуск. */
export async function attachHls(element: HTMLVideoElement, url: string, callbacks: HlsCallbacks): Promise<Hls | null> {
  const { default: HlsLib } = await import('hls.js');
  if (callbacks.isCancelled()) return null;

  if (!HlsLib.isSupported()) {
    element.src = url;
    return null;
  }

  const hls = new HlsLib();

  // Сначала пробуем восстановиться сами: сеть перезапрашиваем, декодер пересоздаём, и только потом сдаёмся.
  let networkRetries = 0;
  let mediaRecovered = false;
  hls.on(HlsLib.Events.ERROR, (_event, data) => {
    if (!data.fatal) return;

    if (data.type === HlsLib.ErrorTypes.NETWORK_ERROR && networkRetries < MAX_NETWORK_RETRIES) {
      networkRetries++;
      // startLoad не перезапрашивает упавший плейлист, поэтому загружаем поток заново с текущего места.
      callbacks.onReload(element.currentTime);
      setTimeout(() => {
        if (!callbacks.isCancelled()) hls.loadSource(url);
      }, NETWORK_RETRY_DELAY_MS * networkRetries);
      return;
    }
    if (data.type === HlsLib.ErrorTypes.MEDIA_ERROR && !mediaRecovered) {
      mediaRecovered = true;
      hls.recoverMediaError();
      return;
    }

    console.warn('Поток не воспроизводится', data);
    callbacks.onFatal(`${data.type} · ${data.details}`);
  });
  hls.loadSource(url);
  hls.attachMedia(element);
  return hls;
}
