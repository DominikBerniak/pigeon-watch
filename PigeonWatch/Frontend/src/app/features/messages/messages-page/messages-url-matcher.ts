import { UrlMatchResult, UrlSegment } from '@angular/router';

export function messagesUrlMatcher(segments: UrlSegment[]): UrlMatchResult | null {
  if (segments[0]?.path !== 'messages') return null;

  if (segments.length === 1) return { consumed: segments };

  if (segments.length !== 3) return null;

  return {
    consumed: segments,
    posParams: { sightingId: segments[1], threadId: segments[2] },
  };
}
